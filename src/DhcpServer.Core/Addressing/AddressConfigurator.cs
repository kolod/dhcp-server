using System;
using System.Collections.Generic;
using System.Linq;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Addressing
{
    public enum ApplyOutcome
    {
        Success,
        SettingsLocked,
        PendingRecord,
        RecordCorrupt,
        RecordNotWritable,
        Failed,
        AddressNotReady
    }

    public sealed class ApplyResult
    {
        public ApplyResult(ApplyOutcome outcome, ApplyStep? failedStep = null, RestoreResult rollback = null)
        {
            Outcome = outcome;
            FailedStep = failedStep;
            Rollback = rollback;
        }

        public ApplyOutcome Outcome { get; }

        public bool Succeeded => Outcome == ApplyOutcome.Success;

        /// <summary>The step that failed (apply sequence).</summary>
        public ApplyStep? FailedStep { get; }

        /// <summary>What the rollback did after a failure past the point of no return; null when nothing was changed.</summary>
        public RestoreResult Rollback { get; }
    }

    public enum RestoreOutcome
    {
        Restored,
        LeftUnchanged,
        AdapterMissing,
        RecordCorrupt,
        NoRecord,
        SettingsLocked,
        Failed
    }

    public sealed class RestoreResult
    {
        public RestoreResult(RestoreOutcome outcome, ApplyStep? failedStep = null, SavedConfiguration record = null)
        {
            Outcome = outcome;
            FailedStep = failedStep;
            Record = record;
        }

        public RestoreOutcome Outcome { get; }

        public ApplyStep? FailedStep { get; }

        /// <summary>The saved record the result is about (for messages); may be null.</summary>
        public SavedConfiguration Record { get; }

        /// <summary>True when nothing is left to do (the record is gone and the adapter is as it should be).</summary>
        public bool IsFinal => Outcome == RestoreOutcome.Restored || Outcome == RestoreOutcome.LeftUnchanged || Outcome == RestoreOutcome.NoRecord;
    }

    /// <summary>
    /// Applies a fixed address to one adapter and puts the previous configuration back (Principle II).
    /// The order of every operation is specified in contracts/apply-sequence.md.
    /// </summary>
    public sealed class AddressConfigurator
    {
        public const int MaxSavedAddresses = 8;
        public static readonly TimeSpan AddressWait = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        private readonly IAdapterConfigService _service;
        private readonly IRestoreStore _store;
        private readonly ISettingsLock _lock;
        private readonly IInterfaceProvider _interfaces;
        private readonly IClock _clock;
        private readonly Action<TimeSpan> _sleep;
        private readonly object _sync = new object();
        private bool _holdsLock;

        public AddressConfigurator(
            IAdapterConfigService service,
            IRestoreStore store,
            ISettingsLock settingsLock,
            IInterfaceProvider interfaces,
            IClock clock,
            Action<TimeSpan> sleep = null)
        {
            _service = service;
            _store = store;
            _lock = settingsLock;
            _interfaces = interfaces;
            _clock = clock;
            _sleep = sleep ?? System.Threading.Thread.Sleep;
        }

        /// <summary>True between a successful apply and the end of the restore (this instance holds the lock).</summary>
        public bool HasAppliedSettings
        {
            get { lock (_sync) { return _holdsLock; } }
        }

        public ApplyResult Apply(InterfaceInfo adapter, AddressSettings settings)
        {
            lock (_sync)
            {
                if (!_lock.TryAcquire())
                {
                    return new ApplyResult(ApplyOutcome.SettingsLocked);
                }

                _holdsLock = true;
            }

            try
            {
                SavedConfiguration pending;
                try
                {
                    pending = _store.Load();
                }
                catch (RestoreRecordCorruptException)
                {
                    return Abort(ApplyOutcome.RecordCorrupt);
                }
                catch (Exception)
                {
                    return Abort(ApplyOutcome.RecordNotWritable);
                }

                if (pending != null)
                {
                    return Abort(ApplyOutcome.PendingRecord);
                }

                var previous = _service.Read(adapter.Id);
                if (previous == null
                    || (previous.AddressMode == AddressMode.Static && previous.Addresses.Count == 0)
                    || previous.Addresses.Count > MaxSavedAddresses)
                {
                    return Abort(ApplyOutcome.Failed, ApplyStep.Capture);
                }

                var target = AdapterConfig.FromSettings(adapter.Id, previous.InterfaceIndex, settings);
                var record = new SavedConfiguration(adapter.Name, previous, target, _clock.UtcNow);
                try
                {
                    _store.Save(record);
                }
                catch (Exception)
                {
                    return Abort(ApplyOutcome.RecordNotWritable, ApplyStep.WriteRecord);
                }

                // From here on the adapter may change: every failure rolls back.
                var failure = RunApplySteps(target);
                if (failure == null)
                {
                    return new ApplyResult(ApplyOutcome.Success);
                }

                var rollback = RestoreCore(true);
                return new ApplyResult(
                    failure.Item2 == ApplyStep.WaitForAddress ? ApplyOutcome.AddressNotReady : ApplyOutcome.Failed,
                    failure.Item2,
                    rollback);
            }
            finally
            {
                lock (_sync)
                {
                    // Success keeps the lock until the restore; everything else released it already or must now.
                    if (_holdsLock && !_applied)
                    {
                        ReleaseLock();
                    }
                }
            }
        }

        private bool _applied;

        /// <summary>Puts the previous configuration back (stop, exit, interface lost, recovery, retry).</summary>
        public RestoreResult Restore()
        {
            lock (_sync)
            {
                if (!_holdsLock)
                {
                    if (!_lock.TryAcquire())
                    {
                        return new RestoreResult(RestoreOutcome.SettingsLocked);
                    }

                    _holdsLock = true;
                }
            }

            return RestoreCore(false);
        }

        private Tuple<bool, ApplyStep> RunApplySteps(AdapterConfig target)
        {
            if (!_service.Execute(ApplyStep.SetAddress, target).Succeeded)
            {
                return Tuple.Create(false, ApplyStep.SetAddress);
            }

            var dnsStep = target.DnsServers.Count == 0 ? ApplyStep.ClearDns : ApplyStep.SetDns;
            if (!_service.Execute(dnsStep, target).Succeeded)
            {
                return Tuple.Create(false, dnsStep);
            }

            if (!AdapterConfig.Equivalent(_service.Read(target.AdapterId), target))
            {
                return Tuple.Create(false, ApplyStep.Verify);
            }

            if (!WaitForAddress(target))
            {
                return Tuple.Create(false, ApplyStep.WaitForAddress);
            }

            lock (_sync)
            {
                _applied = true;
            }

            return null;
        }

        private bool WaitForAddress(AdapterConfig target)
        {
            var wanted = target.Addresses[0].Address;
            var start = _clock.UtcNow;
            while (true)
            {
                var info = _interfaces.GetInterfaces().FirstOrDefault(i => i.Id == target.AdapterId);
                if (info != null && info.IsUp && wanted.Equals(info.Address) && info.IsAddressPreferred)
                {
                    return true;
                }

                if (_clock.UtcNow - start >= AddressWait)
                {
                    return false;
                }

                _sleep(PollInterval);
            }
        }

        private RestoreResult RestoreCore(bool force)
        {
            SavedConfiguration record;
            try
            {
                record = _store.Load();
            }
            catch (RestoreRecordCorruptException)
            {
                return Finish(new RestoreResult(RestoreOutcome.RecordCorrupt));
            }
            catch (Exception)
            {
                return Finish(new RestoreResult(RestoreOutcome.Failed, ApplyStep.Capture));
            }

            if (record == null)
            {
                return Finish(new RestoreResult(RestoreOutcome.NoRecord));
            }

            var current = _service.Read(record.AdapterId);
            if (current == null)
            {
                return Finish(new RestoreResult(RestoreOutcome.AdapterMissing, null, record));
            }

            if (!force && !record.Restoring && !AdapterConfig.Equivalent(current, record.Applied))
            {
                // Someone else changed the adapter: their change wins (FR-009).
                return Finish(DeleteRecord(new RestoreResult(RestoreOutcome.LeftUnchanged, null, record)));
            }

            try
            {
                _store.Save(record.AsRestoring());
            }
            catch (Exception)
            {
                // Best effort: the restore itself matters more than the marker.
            }

            var previous = record.Previous;
            var failedStep = RunRestoreSteps(previous);
            if (failedStep != null)
            {
                return Finish(new RestoreResult(RestoreOutcome.Failed, failedStep, record));
            }

            if (!AdapterConfig.Equivalent(_service.Read(previous.AdapterId), previous))
            {
                return Finish(new RestoreResult(RestoreOutcome.Failed, ApplyStep.Verify, record));
            }

            return Finish(DeleteRecord(new RestoreResult(RestoreOutcome.Restored, null, record)));
        }

        private ApplyStep? RunRestoreSteps(AdapterConfig previous)
        {
            if (previous.AddressMode == AddressMode.Automatic)
            {
                if (!_service.Execute(ApplyStep.SetAddressDhcp, previous).Succeeded)
                {
                    return ApplyStep.SetAddressDhcp;
                }
            }
            else
            {
                if (!_service.Execute(ApplyStep.SetAddress, previous).Succeeded)
                {
                    return ApplyStep.SetAddress;
                }

                for (var i = 1; i < previous.Addresses.Count; i++)
                {
                    if (!_service.Execute(ApplyStep.AddAddress, previous, i).Succeeded)
                    {
                        return ApplyStep.AddAddress;
                    }
                }
            }

            ApplyStep dnsStep;
            if (previous.DnsMode == DnsMode.Automatic)
            {
                dnsStep = ApplyStep.SetDnsDhcp;
            }
            else
            {
                dnsStep = previous.DnsServers.Count == 0 ? ApplyStep.ClearDns : ApplyStep.SetDns;
            }

            return _service.Execute(dnsStep, previous).Succeeded ? (ApplyStep?)null : dnsStep;
        }

        private RestoreResult DeleteRecord(RestoreResult success)
        {
            try
            {
                _store.Delete();
            }
            catch (Exception)
            {
                return new RestoreResult(RestoreOutcome.Failed, ApplyStep.DeleteRecord, success.Record);
            }

            return success;
        }

        private RestoreResult Finish(RestoreResult result)
        {
            lock (_sync)
            {
                _applied = false;
                ReleaseLock();
            }

            return result;
        }

        private ApplyResult Abort(ApplyOutcome outcome, ApplyStep? step = null)
        {
            lock (_sync)
            {
                ReleaseLock();
            }

            return new ApplyResult(outcome, step);
        }

        private void ReleaseLock()
        {
            if (_holdsLock)
            {
                _holdsLock = false;
                _lock.Release();
            }
        }
    }
}
