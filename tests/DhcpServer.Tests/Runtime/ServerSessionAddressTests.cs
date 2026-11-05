using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Runtime
{
    /// <summary>A session wired with the fake adapter service; the fake interface provider follows the adapter's configuration.</summary>
    internal sealed class AddressFixture : IDisposable
    {
        public AddressFixture(AdapterConfig original = null)
        {
            Original = original ?? AddressingTestData.Dhcp();
            Log = Endpoints.Log;
            Service = new FakeAdapterConfigService(Log);
            Store = new InMemoryRestoreStore(Log);
            Lock = new FakeSettingsLock(Log);
            Probe = new FakeDhcpServerProbe(Log);
            Service.Set(Original);
            Sync(Original);
            Service.Changed = Sync;
            Configurator = new AddressConfigurator(Service, Store, Lock, Interfaces, Clock, span => Clock.Advance(span));
            Recovery = new RecoveryService(Configurator, Store);
            Session = Create(Configurator, Recovery);
            Session.StateChanged += (s, e) => States.Add(((ServerSession)s).State);
        }

        public AdapterConfig Original { get; }

        public FakeEndpointFactory Endpoints { get; } = new FakeEndpointFactory();

        public List<string> Log { get; }

        public FakeAdapterConfigService Service { get; }

        public InMemoryRestoreStore Store { get; }

        public FakeSettingsLock Lock { get; }

        public FakeDhcpServerProbe Probe { get; }

        public FakeInterfaceProvider Interfaces { get; } = new FakeInterfaceProvider();

        public FakeClock Clock { get; } = new FakeClock();

        public AddressConfigurator Configurator { get; }

        public RecoveryService Recovery { get; }

        public ServerSession Session { get; }

        public List<ServerState> States { get; } = new List<ServerState>();

        public InterfaceInfo Chosen => Interfaces.Interfaces.First(i => i.Id == "eth0");

        public AdapterConfig Current => Service.Read("eth0");

        public ServerSession Create(AddressConfigurator configurator, RecoveryService recovery = null)
        {
            return new ServerSession(Endpoints, Clock, Interfaces, Probe, new FakeAddressProber(), new InMemoryLeaseStore(), Timeout.InfiniteTimeSpan, configurator, recovery);
        }

        /// <summary>Mirrors an adapter configuration into the fake interface list.</summary>
        public void Sync(AdapterConfig config)
        {
            var first = config.Addresses.FirstOrDefault();
            Interfaces.Replace(TestNet.Eth(config.AdapterId, first?.Address.ToString(), first?.PrefixLength ?? 24, true, config.Gateway?.ToString()));
        }

        public void Start(AddressSettings settings, ServerSession session = null)
        {
            (session ?? Session).StartAsync(Chosen, settings).Join();
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    public class ServerSessionAddressStartTests
    {
        [Fact]
        public void StartWithSettings_RecordApplyGateBind_InThatOrder()
        {
            using (var f = new AddressFixture())
            {
                f.Start(AddressingTestData.Settings());

                Assert.Equal(ServerState.Running, f.Session.State);
                Assert.Equal(new[] { "lock", "record:save", "step:SetAddress", "step:ClearDns", "probe", "bind" }, f.Log.ToArray());
                Assert.Equal("192.168.137.1", f.Endpoints.Bound.Single().ToString());
                Assert.True(AdapterConfig.Equivalent(AdapterConfig.FromSettings("eth0", 7, AddressingTestData.Settings()), f.Current));
            }
        }

        [Fact]
        public void StartWithoutSettings_NeverTouchesAddressServices()
        {
            using (var f = new AddressFixture(AddressingTestData.Static("eth0", "192.168.137.1", 24, null)))
            {
                f.Start(null);

                Assert.Equal(ServerState.Running, f.Session.State);
                Assert.Equal(new[] { "probe", "bind" }, f.Log.ToArray());
                Assert.Empty(f.Service.Calls);
                Assert.Equal(0, f.Store.SaveCount);
            }
        }

        [Fact]
        public void DownAdapter_IsRefusedBeforeAnyChange()
        {
            using (var f = new AddressFixture())
            {
                f.Interfaces.Replace(TestNet.Eth("eth0", "10.20.30.40", up: false));
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.Down, f.Session.Failure.Kind);
                Assert.Empty(f.Log);
                Assert.Equal(0, f.Store.SaveCount);
            }
        }

        [Fact]
        public void OverlappingProposedSubnet_IsRefusedBeforeAnyChange()
        {
            using (var f = new AddressFixture())
            {
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "192.168.137.77"));
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.SubnetOverlap, f.Session.Failure.Kind);
                Assert.Empty(f.Service.Calls);
            }
        }

        [Fact]
        public void UnsupportedAdapterType_IsRefused()
        {
            using (var f = new AddressFixture())
            {
                f.Interfaces.Replace(new InterfaceInfo("eth0", "tun", InterfaceKind.Tunnel, true, IPAddress.Parse("10.20.30.40"), 24));
                f.Start(AddressingTestData.Settings());
                Assert.Equal(FailureKind.NotSupportedType, f.Session.Failure.Kind);
                Assert.Empty(f.Service.Calls);
            }
        }

        [Fact]
        public void FailedApply_NeverRuns_NeverBinds_AndNeverProbes()
        {
            using (var f = new AddressFixture())
            {
                f.Service.FailOn(ApplyStep.SetAddress);
                f.Start(AddressingTestData.Settings());

                Assert.Equal(ServerState.Failed, f.Session.State);
                Assert.Equal(FailureKind.AddressApplyFailed, f.Session.Failure.Kind);
                Assert.Equal("SetAddress", f.Session.Failure.Argument);
                Assert.Empty(f.Endpoints.Bound);
                Assert.Equal(0, f.Probe.Calls);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
                Assert.Null(f.Store.Record);
                Assert.DoesNotContain(ServerState.Running, f.States);
            }
        }

        [Fact]
        public void RecordNotWritable_RefusesWithoutChange()
        {
            using (var f = new AddressFixture())
            {
                f.Store.ThrowOnSave = true;
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.RecordNotWritable, f.Session.Failure.Kind);
                Assert.Empty(f.Service.Calls);
            }
        }

        [Fact]
        public void AddressNeverBecomingReady_FailsAndRestores()
        {
            using (var f = new AddressFixture())
            {
                f.Service.Changed = config =>
                {
                    var first = config.Addresses.FirstOrDefault();
                    f.Interfaces.Replace(new InterfaceInfo("eth0", "Ethernet eth0", InterfaceKind.Ethernet, true, first?.Address, first?.PrefixLength ?? 24, null, null, false));
                };
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.AddressNotReady, f.Session.Failure.Kind);
                Assert.Empty(f.Endpoints.Bound);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            }
        }

        [Fact]
        public void OtherDhcpServer_AfterApply_RollsBack()
        {
            using (var f = new AddressFixture())
            {
                f.Probe.Result = ProbeResult.ServerFound(IPAddress.Parse("192.168.137.254"));
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.OtherDhcpServer, f.Session.Failure.Kind);
                Assert.Empty(f.Endpoints.Bound);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
                Assert.Null(f.Store.Record);
                Assert.False(f.Lock.Held);
            }
        }

        [Fact]
        public void BindFailure_AfterApply_RollsBack()
        {
            using (var f = new AddressFixture())
            {
                f.Endpoints.FailWith = new FailureReason(FailureKind.PortInUse);
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.PortInUse, f.Session.Failure.Kind);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
                Assert.Null(f.Store.Record);
            }
        }

        [Fact]
        public void FailedRollback_ReportsRestoreFailure_WithPreviousSettings()
        {
            using (var f = new AddressFixture())
            {
                f.Probe.Result = ProbeResult.Inconclusive;
                f.Service.FailOn(ApplyStep.SetAddressDhcp);
                f.Start(AddressingTestData.Settings());

                Assert.Equal(FailureKind.AddressRestoreFailed, f.Session.Failure.Kind);
                Assert.Equal("SetAddressDhcp", f.Session.Failure.Argument);
                Assert.NotNull(f.Session.Failure.Previous);
                Assert.NotNull(f.Store.Record);
            }
        }

        [Fact]
        public void CancelWhileProbing_AfterApply_RestoresTheAdapter()
        {
            using (var f = new AddressFixture())
            {
                f.Probe.Gate = new ManualResetEventSlim();
                var task = f.Session.StartAsync(f.Chosen, AddressingTestData.Settings());
                var until = DateTime.UtcNow.AddSeconds(5);
                while (f.Probe.Calls == 0 && DateTime.UtcNow < until)
                {
                    Thread.Sleep(5);
                }

                f.Session.CancelStart();
                task.Join(5000);

                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Empty(f.Endpoints.Bound);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
                Assert.Null(f.Store.Record);
            }
        }

        [Fact]
        public void SecondInstance_CannotChangeSettings_WhileTheFirstHoldsTheLock()
        {
            using (var f = new AddressFixture())
            {
                f.Start(AddressingTestData.Settings());
                f.Service.Calls.Clear();

                var otherConfigurator = new AddressConfigurator(f.Service, f.Store, new FakeSettingsLock { Refuse = true }, f.Interfaces, f.Clock, s => { });
                using (var other = f.Create(otherConfigurator))
                {
                    other.StartAsync(f.Chosen, AddressingTestData.Settings("192.168.150.1")).Join();
                    Assert.Equal(FailureKind.SettingsLocked, other.Failure.Kind);
                }

                Assert.Empty(f.Service.Calls);
            }
        }

        [Fact]
        public void AllCalls_UseOnlyTheChosenAdapter()
        {
            using (var f = new AddressFixture())
            {
                f.Service.Set(AddressingTestData.Static("eth1", "172.16.0.9", 16, "172.16.0.1", "9.9.9.9"));
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "172.16.0.9", 16));
                f.Start(AddressingTestData.Settings("192.168.137.1", 24, null, "8.8.8.8"));
                f.Session.Stop();

                Assert.NotEmpty(f.Service.Calls);
                Assert.All(f.Service.Calls, c => Assert.Equal("eth0", c.Value));
            }
        }

        [Fact]
        public void PendingRecovery_BlocksStart()
        {
            using (var f = new AddressFixture())
            {
                f.Store.Record = new SavedConfiguration("x", f.Original, f.Original, DateTime.UtcNow);
                f.Service.Adapters.Clear(); // restore cannot finish => pending
                Assert.Equal(RecoveryState.Pending, f.Recovery.Run());

                f.Service.Set(f.Original);
                f.Start(AddressingTestData.Settings());
                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.DoesNotContain(f.Log, l => l == "probe" || l == "bind");

                f.Recovery.Discard();
                f.Start(AddressingTestData.Settings());
                Assert.Equal(ServerState.Running, f.Session.State);
            }
        }
    }

    public class ServerSessionAddressStopTests
    {
        [Fact]
        public void Stop_ClosesEndpointFirst_ThenRestores()
        {
            using (var f = new AddressFixture(AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1", "8.8.8.8")))
            {
                f.Start(AddressingTestData.Settings());
                f.Log.Clear();
                var restored = new List<RestoreResult>();
                f.Session.RestoreCompleted += (s, r) => restored.Add(r);

                f.Session.Stop();

                Assert.Equal("close", f.Log.First());
                Assert.True(f.Log.IndexOf("close") < f.Log.IndexOf("step:SetAddress"));
                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
                Assert.Null(f.Store.Record);
                Assert.Equal(RestoreOutcome.Restored, restored.Single().Outcome);
            }
        }

        [Fact]
        public void StateGoesThroughStopping()
        {
            using (var f = new AddressFixture())
            {
                f.Start(AddressingTestData.Settings());
                f.States.Clear();
                f.Session.Stop();
                Assert.Equal(new[] { ServerState.Stopping, ServerState.Stopped }, f.States.ToArray());
            }
        }

        [Fact]
        public void Dispose_RestoresToo()
        {
            var f = new AddressFixture(AddressingTestData.Static());
            f.Start(AddressingTestData.Settings());
            var endpoint = f.Endpoints.Last;
            f.Dispose();
            Assert.True(endpoint.IsClosed);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            Assert.Null(f.Store.Record);
        }

        [Fact]
        public void InterfaceLost_ClosesFirst_ThenRestores_WhenTheAdapterStillExists()
        {
            using (var f = new AddressFixture(AddressingTestData.Static()))
            {
                f.Start(AddressingTestData.Settings());
                f.Log.Clear();
                f.Interfaces.Interfaces.Clear();

                f.Session.Tick();

                Assert.Equal(FailureKind.InterfaceLost, f.Session.Failure.Kind);
                Assert.True(f.Log.IndexOf("close") < f.Log.IndexOf("step:SetAddress"));
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            }
        }

        [Fact]
        public void InterfaceLost_AdapterGone_KeepsTheRecord()
        {
            using (var f = new AddressFixture(AddressingTestData.Static()))
            {
                f.Start(AddressingTestData.Settings());
                var restored = new List<RestoreResult>();
                f.Session.RestoreCompleted += (s, r) => restored.Add(r);
                f.Service.Adapters.Clear();
                f.Interfaces.Interfaces.Clear();

                f.Session.Tick();

                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Equal(RestoreOutcome.AdapterMissing, restored.Single().Outcome);
                Assert.NotNull(f.Store.Record);
            }
        }

        [Fact]
        public void RestoreFailure_StillEndsStopped_WithTheRecordKept()
        {
            using (var f = new AddressFixture(AddressingTestData.Dhcp()))
            {
                f.Start(AddressingTestData.Settings());
                f.Service.FailOn(ApplyStep.SetAddressDhcp);

                f.Session.Stop();

                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Equal(FailureKind.AddressRestoreFailed, f.Session.Failure.Kind);
                Assert.Equal("SetAddressDhcp", f.Session.Failure.Argument);
                Assert.NotNull(f.Session.Failure.Previous);
                Assert.NotNull(f.Store.Record);
            }
        }

        [Fact]
        public void SessionWithoutSettings_StopNeverRestores()
        {
            using (var f = new AddressFixture(AddressingTestData.Static("eth0", "192.168.137.1", 24, null)))
            {
                f.Start(null);
                f.Session.Stop();
                Assert.Empty(f.Service.Calls);
            }
        }

        [Fact]
        public void ChangedBySomeoneElse_DuringSession_IsLeftAlone()
        {
            using (var f = new AddressFixture(AddressingTestData.Static()))
            {
                f.Start(AddressingTestData.Settings());
                var theirs = AddressingTestData.Static("eth0", "10.99.99.9", 24, null);
                f.Service.Set(theirs);
                var restored = new List<RestoreResult>();
                f.Session.RestoreCompleted += (s, r) => restored.Add(r);

                f.Session.Stop();

                Assert.Equal(RestoreOutcome.LeftUnchanged, restored.Single().Outcome);
                Assert.True(AdapterConfig.Equivalent(theirs, f.Current));
            }
        }
    }

    public class RecoveryTests
    {
        private static AddressFixture Crashed(AdapterConfig original = null)
        {
            var f = new AddressFixture(original ?? AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1", "8.8.8.8"));
            Assert.True(f.Configurator.Apply(f.Chosen, AddressingTestData.Settings()).Succeeded);
            // the process "dies": a new program instance starts with a fresh lock and configurator
            return f;
        }

        private static RecoveryService NewInstance(AddressFixture f, FakeSettingsLock settingsLock = null)
        {
            var configurator = new AddressConfigurator(f.Service, f.Store, settingsLock ?? new FakeSettingsLock(), f.Interfaces, f.Clock, s => { });
            return new RecoveryService(configurator, f.Store);
        }

        [Fact]
        public void NothingToDo_WithoutRecord()
        {
            using (var f = new AddressFixture())
            {
                var recovery = NewInstance(f);
                Assert.Equal(RecoveryState.NothingToDo, recovery.Run());
                Assert.True(recovery.CanStartSession);
                Assert.Empty(f.Service.Calls);
            }
        }

        [Fact]
        public void CrashedSession_IsRestoredAtStart()
        {
            using (var f = Crashed())
            {
                var recovery = NewInstance(f);
                Assert.False(AdapterConfig.Equivalent(f.Original, f.Current));

                Assert.Equal(RecoveryState.Done, recovery.Run());

                Assert.Equal(RestoreOutcome.Restored, recovery.LastResult.Outcome);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
                Assert.Null(f.Store.Record);
                Assert.True(recovery.CanStartSession);
            }
        }

        [Fact]
        public void CrashedAutomaticAdapter_ComesBackAutomatic()
        {
            using (var f = Crashed(AddressingTestData.Dhcp()))
            {
                NewInstance(f).Run();
                Assert.Equal(AddressMode.Automatic, f.Current.AddressMode);
                Assert.Equal(DnsMode.Automatic, f.Current.DnsMode);
            }
        }

        [Fact]
        public void FailedRestore_BlocksStart_UntilRetrySucceeds()
        {
            using (var f = Crashed())
            {
                var recovery = NewInstance(f);
                f.Service.FailOn(ApplyStep.SetAddress, 2);

                Assert.Equal(RecoveryState.Pending, recovery.Run());
                Assert.False(recovery.CanStartSession);
                Assert.NotNull(recovery.PendingRecord);
                Assert.Equal(ApplyStep.SetAddress, recovery.LastResult.FailedStep);

                Assert.Equal(RecoveryState.Done, recovery.Retry());
                Assert.True(recovery.CanStartSession);
                Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            }
        }

        [Fact]
        public void AdapterMissing_IsPending_AndDiscardClearsIt()
        {
            using (var f = Crashed())
            {
                var recovery = NewInstance(f);
                f.Service.Adapters.Clear();

                Assert.Equal(RecoveryState.Pending, recovery.Run());
                Assert.False(recovery.CanStartSession);

                Assert.Equal(RecoveryState.Done, recovery.Discard());
                Assert.Null(f.Store.Record);
                Assert.True(recovery.CanStartSession);
            }
        }

        [Fact]
        public void CorruptRecord_OnlyDiscardHelps()
        {
            using (var f = new AddressFixture())
            {
                f.Store.Corrupt = true;
                var recovery = NewInstance(f);

                Assert.Equal(RecoveryState.Corrupt, recovery.Run());
                Assert.False(recovery.CanStartSession);
                Assert.Empty(f.Service.Calls);
                Assert.Equal(RecoveryState.Corrupt, recovery.Retry());

                Assert.Equal(RecoveryState.Done, recovery.Discard());
                Assert.True(recovery.CanStartSession);
            }
        }

        [Fact]
        public void LockHeldByAnotherInstance_ChangesNothing()
        {
            using (var f = Crashed())
            {
                f.Service.Calls.Clear();
                var recovery = NewInstance(f, new FakeSettingsLock { Refuse = true });

                Assert.Equal(RecoveryState.Pending, recovery.Run());
                Assert.Equal(RestoreOutcome.SettingsLocked, recovery.LastResult.Outcome);
                Assert.Empty(f.Service.Calls);
                Assert.NotNull(f.Store.Record);
            }
        }

        [Fact]
        public void AdapterChangedByUser_IsLeftAlone_AndStartAllowed()
        {
            using (var f = Crashed())
            {
                f.Service.Set(AddressingTestData.Static("eth0", "10.99.99.9", 24, null));
                f.Service.Calls.Clear();
                var recovery = NewInstance(f);

                Assert.Equal(RecoveryState.Done, recovery.Run());
                Assert.Equal(RestoreOutcome.LeftUnchanged, recovery.LastResult.Outcome);
                Assert.Empty(f.Service.Calls);
                Assert.Null(f.Store.Record);
            }
        }
    }
}
