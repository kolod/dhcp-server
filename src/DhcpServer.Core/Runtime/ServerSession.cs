using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Dhcp;
using DhcpServer.Core.Leases;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Runtime
{
    /// <summary>
    /// One server run on one chosen interface: optional fixed address for the PC, start gate, socket,
    /// receive loop, interface monitoring, lease persistence and — when settings were changed — the
    /// restore of the previous configuration. Thread-safe; events may fire on background threads.
    /// </summary>
    public sealed class ServerSession : IDisposable
    {
        private static readonly TimeSpan SaveThrottle = TimeSpan.FromSeconds(1);

        private readonly IEndpointFactory _endpoints;
        private readonly IClock _clock;
        private readonly IInterfaceProvider _interfaces;
        private readonly IDhcpServerProbe _probe;
        private readonly IAddressProber _prober;
        private readonly ILeaseStore _store;
        private readonly AddressConfigurator _configurator;
        private readonly RecoveryService _recovery;

        private readonly object _sync = new object();
        private readonly object _engineLock = new object();
        private readonly object _saveLock = new object();

        private ServerState _state = ServerState.Stopped;
        private FailureReason _failure;
        private InterfaceInfo _chosen;
        private DhcpEngine _engine;
        private IDhcpEndpoint _endpoint;
        private Thread _thread;
        private CancellationTokenSource _cts;
        private Timer _timer;
        private bool _disposed;
        private bool _addressApplied;
        private int _packetsReceived;

        private DateTime _lastSave = DateTime.MinValue;
        private bool _savePending;

        /// <param name="pollInterval">How often the chosen interface is re-checked; <see cref="Timeout.InfiniteTimeSpan"/> disables the timer (tests call <see cref="Tick"/>).</param>
        /// <param name="configurator">Needed only to start with a fixed address; null disables that path.</param>
        /// <param name="recovery">When given and a restore is pending, starting is refused.</param>
        public ServerSession(
            IEndpointFactory endpoints,
            IClock clock,
            IInterfaceProvider interfaces,
            IDhcpServerProbe probe,
            IAddressProber prober,
            ILeaseStore store,
            TimeSpan pollInterval,
            AddressConfigurator configurator = null,
            RecoveryService recovery = null)
        {
            _endpoints = endpoints;
            _clock = clock;
            _interfaces = interfaces;
            _probe = probe;
            _prober = prober;
            _store = store;
            _configurator = configurator;
            _recovery = recovery;
            PollInterval = pollInterval;

            Leases = new LeaseTable();
            try
            {
                Leases.Load(_store.Load());
            }
            catch (Exception)
            {
                StoreFailedOnLoad = true;
            }

            Leases.Changed += OnLeasesChanged;
            _interfaces.Changed += OnInterfacesChanged;
        }

        public event EventHandler StateChanged;

        public event EventHandler PoolExhausted;

        /// <summary>Raised when saving leases failed; leases then live in memory only.</summary>
        public event EventHandler StoreFailed;

        /// <summary>Raised after settings were restored (or could not be) at the end of a run or a rollback.</summary>
        public event EventHandler<RestoreResult> RestoreCompleted;

        public LeaseTable Leases { get; }

        public TimeSpan PollInterval { get; }

        public bool StoreFailedOnLoad { get; }

        /// <summary>Datagrams received since the session object was created (used for the firewall hint).</summary>
        public int PacketsReceived => Volatile.Read(ref _packetsReceived);

        public ServerState State
        {
            get { lock (_sync) { return _state; } }
        }

        /// <summary>Why the last start was refused or the run ended abnormally; null otherwise.</summary>
        public FailureReason Failure
        {
            get { lock (_sync) { return _failure; } }
        }

        /// <summary>The interface of the current run (valid while Starting/Running).</summary>
        public InterfaceInfo Chosen
        {
            get { lock (_sync) { return _chosen; } }
        }

        /// <summary>
        /// Starts in the background (applying settings and the probe take seconds). No-op unless Stopped or
        /// Failed, and while a restore from an earlier crash is pending. With <paramref name="settings"/> the
        /// PC's adapter gets that fixed address first (the caller has shown the preview and got consent).
        /// </summary>
        public Task StartAsync(InterfaceInfo chosen, AddressSettings settings = null)
        {
            CancellationToken token;
            lock (_sync)
            {
                if (_disposed || _state == ServerState.Starting || _state == ServerState.Running || _state == ServerState.Stopping
                    || (_recovery != null && !_recovery.CanStartSession))
                {
                    return Task.FromResult(0);
                }

                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                token = _cts.Token;
                _failure = null;
                _chosen = chosen;
                _state = ServerState.Starting;
            }

            RaiseStateChanged();
            return Task.Run(() => StartCore(chosen, settings, token));
        }

        /// <summary>Aborts a start in progress (the probe is cancelled, nothing is bound).</summary>
        public void CancelStart()
        {
            var changed = false;
            lock (_sync)
            {
                if (_state == ServerState.Starting)
                {
                    _cts?.Cancel();
                    _state = ServerState.Stopped;
                    changed = true;
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }

        /// <summary>Stops answering immediately: the socket is closed before anything else happens.</summary>
        public void Stop()
        {
            CancelStart();
            StopRunning(null);
            lock (_sync)
            {
                if (_state == ServerState.Failed)
                {
                    _state = ServerState.Stopped;
                    _failure = null;
                }
                else
                {
                    return;
                }
            }

            RaiseStateChanged();
        }

        /// <summary>Re-checks the chosen interface and flushes pending lease writes.</summary>
        public void Tick()
        {
            CheckInterface();
            FlushPendingSave();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _interfaces.Changed -= OnInterfacesChanged;
            Stop();
            Leases.Changed -= OnLeasesChanged;
        }

        private void StartCore(InterfaceInfo chosen, AddressSettings settings, CancellationToken token)
        {
            var applied = false;
            try
            {
                if (settings != null)
                {
                    var refusal = ApplySettings(chosen, settings, token, out applied);
                    if (refusal != null)
                    {
                        Fail(refusal, token);
                        return;
                    }

                    if (token.IsCancellationRequested)
                    {
                        RollbackSettings();
                        return;
                    }
                }

                var gate = StartGate.Check(chosen, _interfaces, _probe, token);
                if (gate.Cancelled || token.IsCancellationRequested)
                {
                    if (applied)
                    {
                        RollbackSettings();
                    }

                    return;
                }

                if (gate.Failure != null)
                {
                    Fail(applied ? WithRollback(gate.Failure) : gate.Failure, token);
                    return;
                }

                var info = gate.Interface;
                var pool = new AddressPool(info.Address, info.PrefixLength, info.Gateway);
                var engine = new DhcpEngine(pool, Leases, address => _prober != null && _prober.IsInUse(address, info.Address));
                engine.PoolExhausted += (s, e) => PoolExhausted?.Invoke(this, EventArgs.Empty);

                var endpoint = _endpoints.Bind(info.Address);
                lock (_sync)
                {
                    if (token.IsCancellationRequested)
                    {
                        endpoint.Close();
                        if (applied)
                        {
                            // fall through to the rollback below, outside the lock
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        _chosen = info;
                        _engine = engine;
                        _endpoint = endpoint;
                        var bound = endpoint;
                        _thread = new Thread(() => ReceiveLoop(bound, engine)) { IsBackground = true, Name = "DhcpReceive" };
                        _thread.Start();
                        if (PollInterval != Timeout.InfiniteTimeSpan)
                        {
                            _timer = new Timer(_ => Tick(), null, PollInterval, PollInterval);
                        }

                        _state = ServerState.Running;
                        endpoint = null;
                    }
                }

                if (endpoint != null)
                {
                    RollbackSettings();
                    return;
                }

                RaiseStateChanged();
            }
            catch (EndpointBindException e)
            {
                var reason = e.Reason ?? new FailureReason(FailureKind.Unexpected, e.Message);
                Fail(applied ? WithRollback(reason) : reason, token);
            }
            catch (Exception e)
            {
                var reason = new FailureReason(FailureKind.Unexpected, e.Message);
                Fail(applied ? WithRollback(reason) : reason, token);
            }
        }

        /// <summary>Returns null on success (settings applied), otherwise the reason to refuse the start.</summary>
        private FailureReason ApplySettings(InterfaceInfo chosen, AddressSettings settings, CancellationToken token, out bool applied)
        {
            applied = false;
            if (_configurator == null)
            {
                return new FailureReason(FailureKind.Unexpected, "address configuration is not available");
            }

            var all = _interfaces.GetInterfaces();
            var fresh = all.FirstOrDefault(i => i.Id == chosen.Id);
            if (fresh == null || !fresh.IsUp)
            {
                return new FailureReason(FailureKind.Down);
            }

            if (!InterfaceEligibility.IsConfigurable(fresh))
            {
                return new FailureReason(FailureKind.NotSupportedType);
            }

            if (AddressSettingsValidator.Overlaps(settings.Address, settings.PrefixLength, fresh.Id, all))
            {
                return new FailureReason(FailureKind.SubnetOverlap);
            }

            var result = _configurator.Apply(fresh, settings);
            if (result.Succeeded)
            {
                lock (_sync)
                {
                    _addressApplied = true;
                }

                applied = true;
                return null;
            }

            if (result.Rollback != null && !result.Rollback.IsFinal)
            {
                return RestoreFailure(result.Rollback);
            }

            switch (result.Outcome)
            {
                case ApplyOutcome.SettingsLocked:
                case ApplyOutcome.PendingRecord:
                case ApplyOutcome.RecordCorrupt:
                    return new FailureReason(FailureKind.SettingsLocked);
                case ApplyOutcome.RecordNotWritable:
                    return new FailureReason(FailureKind.RecordNotWritable);
                case ApplyOutcome.AddressNotReady:
                    return new FailureReason(FailureKind.AddressNotReady);
                default:
                    return new FailureReason(FailureKind.AddressApplyFailed, (result.FailedStep ?? ApplyStep.Capture).ToString());
            }
        }

        /// <summary>After a later start step failed: put the old settings back and keep the original reason if that worked.</summary>
        private FailureReason WithRollback(FailureReason original)
        {
            var restore = RollbackSettings();
            return restore != null && !restore.IsFinal ? RestoreFailure(restore) : original;
        }

        private RestoreResult RollbackSettings()
        {
            if (_configurator == null)
            {
                return null;
            }

            bool needed;
            lock (_sync)
            {
                needed = _addressApplied;
                _addressApplied = false;
            }

            if (!needed)
            {
                return null;
            }

            var result = _configurator.Restore();
            RestoreCompleted?.Invoke(this, result);
            return result;
        }

        private static FailureReason RestoreFailure(RestoreResult restore)
        {
            return new FailureReason(
                FailureKind.AddressRestoreFailed,
                (restore.FailedStep ?? ApplyStep.Capture).ToString(),
                restore.Record?.Previous);
        }

        private void Fail(FailureReason reason, CancellationToken token)
        {
            lock (_sync)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _failure = reason;
                _state = ServerState.Failed;
            }

            RaiseStateChanged();
        }

        private void ReceiveLoop(IDhcpEndpoint endpoint, DhcpEngine engine)
        {
            while (true)
            {
                byte[] data;
                try
                {
                    if (!endpoint.TryReceive(out data))
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                    return;
                }

                Interlocked.Increment(ref _packetsReceived);
                try
                {
                    byte[] reply;
                    lock (_engineLock)
                    {
                        reply = engine.Handle(data, _clock.UtcNow);
                    }

                    if (reply != null)
                    {
                        endpoint.SendBroadcast(reply);
                    }
                }
                catch (Exception)
                {
                    // Malformed traffic or a socket closed while replying must never end the loop.
                }
            }
        }

        private void StopRunning(FailureReason reason)
        {
            IDhcpEndpoint endpoint;
            Thread thread;
            Timer timer;
            lock (_sync)
            {
                if (_state != ServerState.Running || _endpoint == null)
                {
                    return;
                }

                endpoint = _endpoint;
                thread = _thread;
                timer = _timer;
                _endpoint = null;
                _thread = null;
                _timer = null;
                _engine = null;
            }

            // Close first: from here on no packet is answered.
            endpoint.Close();
            timer?.Dispose();
            if (thread != null && thread != Thread.CurrentThread)
            {
                thread.Join(TimeSpan.FromSeconds(2));
            }

            SaveNow(force: true);

            bool restoreNeeded;
            lock (_sync)
            {
                restoreNeeded = _addressApplied && _configurator != null;
                if (restoreNeeded)
                {
                    _state = ServerState.Stopping;
                }
            }

            RestoreResult restore = null;
            if (restoreNeeded)
            {
                RaiseStateChanged();
                restore = RollbackSettings();
            }

            lock (_sync)
            {
                _failure = restore != null && restore.Outcome == RestoreOutcome.Failed ? RestoreFailure(restore) : reason;
                _state = ServerState.Stopped;
            }

            RaiseStateChanged();
        }

        private void CheckInterface()
        {
            InterfaceInfo chosen;
            lock (_sync)
            {
                if (_state != ServerState.Running)
                {
                    return;
                }

                chosen = _chosen;
            }

            try
            {
                var all = _interfaces.GetInterfaces();
                var fresh = all.FirstOrDefault(i => i.Id == chosen.Id);
                if (fresh == null || !fresh.IsUp || !Equals(fresh.Address, chosen.Address) || fresh.PrefixLength != chosen.PrefixLength
                    || !InterfaceEligibility.Evaluate(fresh, all).IsEligible)
                {
                    StopRunning(new FailureReason(FailureKind.InterfaceLost));
                }
            }
            catch (Exception)
            {
                // A failing adapter query is not proof that the interface is gone; the next tick retries.
            }
        }

        private void OnInterfacesChanged(object sender, EventArgs e)
        {
            CheckInterface();
        }

        private void OnLeasesChanged(object sender, EventArgs e)
        {
            lock (_saveLock)
            {
                if (_clock.UtcNow - _lastSave >= SaveThrottle)
                {
                    SaveLocked();
                }
                else
                {
                    _savePending = true;
                }
            }
        }

        private void FlushPendingSave()
        {
            lock (_saveLock)
            {
                if (_savePending && _clock.UtcNow - _lastSave >= SaveThrottle)
                {
                    SaveLocked();
                }
            }
        }

        private void SaveNow(bool force)
        {
            lock (_saveLock)
            {
                if (force || _savePending)
                {
                    SaveLocked();
                }
            }
        }

        private void SaveLocked()
        {
            _lastSave = _clock.UtcNow;
            _savePending = false;
            try
            {
                _store.Save(Leases.Snapshot());
            }
            catch (Exception)
            {
                StoreFailed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
