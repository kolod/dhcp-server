using System;
using System.Linq;
using System.Net;
using System.Threading;
using DhcpServer.Core.Dhcp;
using DhcpServer.Core.Leases;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Runtime
{
    internal class SessionFixture : IDisposable
    {
        public SessionFixture()
        {
            Endpoints = new FakeEndpointFactory();
            Probe = new FakeDhcpServerProbe(Endpoints.Log);
            Interfaces = new FakeInterfaceProvider();
            Interfaces.Interfaces.Add(TestNet.Eth());
            Session = Create();
        }

        public FakeClock Clock { get; } = new FakeClock();

        public FakeEndpointFactory Endpoints { get; }

        public FakeDhcpServerProbe Probe { get; }

        public FakeInterfaceProvider Interfaces { get; }

        public FakeAddressProber Prober { get; } = new FakeAddressProber();

        public InMemoryLeaseStore Store { get; } = new InMemoryLeaseStore();

        public ServerSession Session { get; private set; }

        public InterfaceInfo Chosen => Interfaces.Interfaces[0];

        public ServerSession Create()
        {
            return new ServerSession(Endpoints, Clock, Interfaces, Probe, Prober, Store, Timeout.InfiniteTimeSpan);
        }

        public void Start(InterfaceInfo info = null)
        {
            Session.StartAsync(info ?? Chosen).Wait(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    public class ServerSessionTests
    {
        [Fact]
        public void NewSession_DoesNotStartByItself()
        {
            using (var f = new SessionFixture())
            {
                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Empty(f.Endpoints.Bound);
                Assert.Equal(0, f.Probe.Calls);
            }
        }

        [Fact]
        public void Start_WithoutInterface_FailsAndBindsNothing()
        {
            using (var f = new SessionFixture())
            {
                f.Session.StartAsync(null).Join();
                Assert.Equal(ServerState.Failed, f.Session.State);
                Assert.Equal(FailureKind.NoInterfaceChosen, f.Session.Failure.Kind);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void Start_BindsOnlyTheChosenInterfaceAddress_AndAnswers()
        {
            using (var f = new SessionFixture())
            {
                f.Start();

                Assert.Equal(ServerState.Running, f.Session.State);
                Assert.Equal(new[] { "192.168.137.1" }, f.Endpoints.Bound.Select(a => a.ToString()).ToArray());

                f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                Assert.True(f.Endpoints.Last.WaitForSent(1));
                var offer = TestPackets.Parse(f.Endpoints.Last.Sent[0]);
                Assert.Equal(DhcpMessageType.Offer, offer.MessageType);
            }
        }

        [Fact]
        public void Stop_ClosesEndpoint_AndSetsStopped()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                var endpoint = f.Endpoints.Last;
                f.Session.Stop();

                Assert.True(endpoint.IsClosed);
                Assert.Equal(ServerState.Stopped, f.Session.State);
            }
        }

        [Fact]
        public void Stop_ClosesEndpointBeforeStateChanges()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                var endpoint = f.Endpoints.Last;
                var closedWhenNotified = false;
                f.Session.StateChanged += (s, e) =>
                {
                    if (f.Session.State == ServerState.Stopped)
                    {
                        closedWhenNotified = endpoint.IsClosed;
                    }
                };
                f.Session.Stop();
                Assert.True(closedWhenNotified);
            }
        }

        [Fact]
        public void Dispose_ClosesEndpoint()
        {
            var f = new SessionFixture();
            f.Start();
            var endpoint = f.Endpoints.Last;
            f.Dispose();
            Assert.True(endpoint.IsClosed);
        }

        [Fact]
        public void NoReplyIsSent_AfterStop()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                var endpoint = f.Endpoints.Last;
                f.Session.Stop();
                endpoint.Enqueue(TestPackets.Discover(1));
                Thread.Sleep(50);
                Assert.Empty(endpoint.Sent);
            }
        }

        [Theory]
        [InlineData(FailureKind.PortInUse)]
        [InlineData(FailureKind.AccessDenied)]
        public void BindFailure_BecomesFailedWithThatReason(FailureKind kind)
        {
            using (var f = new SessionFixture())
            {
                f.Endpoints.FailWith = new FailureReason(kind);
                f.Start();
                Assert.Equal(ServerState.Failed, f.Session.State);
                Assert.Equal(kind, f.Session.Failure.Kind);
            }
        }

        [Fact]
        public void CanStartAgain_AfterFailure()
        {
            using (var f = new SessionFixture())
            {
                f.Endpoints.FailWith = new FailureReason(FailureKind.PortInUse);
                f.Start();
                f.Endpoints.FailWith = null;
                f.Start();
                Assert.Equal(ServerState.Running, f.Session.State);
                Assert.Null(f.Session.Failure);
            }
        }

        [Fact]
        public void SecondStart_WhileRunning_IsIgnored()
        {
            using (var f = new SessionFixture())
            {
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "10.0.0.1"));
                f.Start();
                f.Start(f.Interfaces.Interfaces[1]);
                Assert.Equal(new[] { "192.168.137.1" }, f.Endpoints.Bound.Select(a => a.ToString()).ToArray());
            }
        }

        [Fact]
        public void OnlyChosenInterfaceAddress_IsEverBound_WithSeveralInterfaces()
        {
            using (var f = new SessionFixture())
            {
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "10.0.0.1"));
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth2", "172.16.0.1", 16));
                f.Start(f.Interfaces.Interfaces[1]);
                Assert.Equal(new[] { "10.0.0.1" }, f.Endpoints.Bound.Select(a => a.ToString()).ToArray());
            }
        }

        [Fact]
        public void PoolExhausted_IsReported()
        {
            using (var f = new SessionFixture())
            {
                f.Interfaces.Replace(TestNet.Eth(ip: "10.0.0.1", prefix: 30));
                var raised = new ManualResetEventSlim();
                f.Session.PoolExhausted += (s, e) => raised.Set();
                f.Start();
                f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                f.Endpoints.Last.WaitForSent(1);
                f.Endpoints.Last.Enqueue(TestPackets.RequestSelecting(1, "10.0.0.2", "10.0.0.1"));
                f.Endpoints.Last.WaitForSent(2);
                f.Endpoints.Last.Enqueue(TestPackets.Discover(2));
                Assert.True(raised.Wait(3000));
            }
        }
    }

    public class ServerSessionSafetyTests
    {
        [Fact]
        public void OtherDhcpServer_RefusesStart_AndBindsNothing()
        {
            using (var f = new SessionFixture())
            {
                f.Probe.Result = DhcpServer.Core.Abstractions.ProbeResult.ServerFound(IPAddress.Parse("192.168.137.254"));
                f.Start();
                Assert.Equal(ServerState.Failed, f.Session.State);
                Assert.Equal(FailureKind.OtherDhcpServer, f.Session.Failure.Kind);
                Assert.Equal("192.168.137.254", f.Session.Failure.Argument);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void InconclusiveProbe_RefusesStart()
        {
            using (var f = new SessionFixture())
            {
                f.Probe.Result = DhcpServer.Core.Abstractions.ProbeResult.Inconclusive;
                f.Start();
                Assert.Equal(FailureKind.ProbeInconclusive, f.Session.Failure.Kind);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void ProbeRunsBeforeBind()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                Assert.Equal(new[] { "probe", "bind" }, f.Endpoints.Log.Take(2).ToArray());
            }
        }

        [Fact]
        public void DownInterface_RefusesStart_WithoutProbing()
        {
            using (var f = new SessionFixture())
            {
                f.Interfaces.Replace(TestNet.Eth(up: false));
                f.Start();
                Assert.Equal(FailureKind.Down, f.Session.Failure.Kind);
                Assert.Equal(0, f.Probe.Calls);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void OverlappingSubnet_RefusesStart()
        {
            using (var f = new SessionFixture())
            {
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "192.168.137.50"));
                f.Start();
                Assert.Equal(FailureKind.SubnetOverlap, f.Session.Failure.Kind);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void LinkLocalAndNoAddress_RefuseStart()
        {
            using (var f = new SessionFixture())
            {
                f.Interfaces.Replace(TestNet.Eth(ip: "169.254.1.1", prefix: 16));
                f.Start();
                Assert.Equal(FailureKind.LinkLocalAddress, f.Session.Failure.Kind);

                f.Interfaces.Replace(TestNet.Eth(ip: null));
                f.Start();
                Assert.Equal(FailureKind.NoIpv4Address, f.Session.Failure.Kind);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void UnknownInterface_RefusesStart()
        {
            using (var f = new SessionFixture())
            {
                var ghost = TestNet.Eth("ghost", "10.9.9.1");
                f.Start(ghost);
                Assert.Equal(ServerState.Failed, f.Session.State);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void CancelWhileProbing_ReturnsToStopped_WithoutBinding()
        {
            using (var f = new SessionFixture())
            {
                f.Probe.Gate = new ManualResetEventSlim();
                var task = f.Session.StartAsync(f.Chosen);
                Assert.Equal(ServerState.Starting, f.Session.State);

                f.Session.CancelStart();
                try
                {
                    task.Join(5000);
                }
                catch (AggregateException)
                {
                }

                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Empty(f.Endpoints.Bound);
            }
        }

        [Fact]
        public void UsedAddress_IsNotOffered_ThroughSession()
        {
            using (var f = new SessionFixture())
            {
                f.Prober.InUse.Add("192.168.137.2");
                f.Start();
                f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                Assert.True(f.Endpoints.Last.WaitForSent(1));
                Assert.Equal("192.168.137.3", TestPackets.Parse(f.Endpoints.Last.Sent[0]).YiAddr.ToString());
            }
        }
    }

    public class InterfaceLossTests
    {
        private static void Replied(SessionFixture f)
        {
            f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
            Assert.True(f.Endpoints.Last.WaitForSent(1));
        }

        [Fact]
        public void InterfaceRemoved_StopsServer_WithReason()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                var endpoint = f.Endpoints.Last;
                f.Interfaces.Interfaces.Clear();
                f.Session.Tick();

                Assert.True(endpoint.IsClosed);
                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Equal(FailureKind.InterfaceLost, f.Session.Failure.Kind);
            }
        }

        [Fact]
        public void InterfaceDown_ViaProviderEvent_StopsServer()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                f.Interfaces.Replace(TestNet.Eth(up: false));
                f.Interfaces.RaiseChanged();
                Assert.Equal(ServerState.Stopped, f.Session.State);
                Assert.Equal(FailureKind.InterfaceLost, f.Session.Failure.Kind);
            }
        }

        [Fact]
        public void AddressChanged_StopsServer()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                f.Interfaces.Replace(TestNet.Eth(ip: "192.168.137.9"));
                f.Session.Tick();
                Assert.Equal(ServerState.Stopped, f.Session.State);
            }
        }

        [Fact]
        public void OverlapAppearing_StopsServer()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "192.168.137.200"));
                f.Session.Tick();
                Assert.Equal(ServerState.Stopped, f.Session.State);
            }
        }

        [Fact]
        public void UnrelatedInterfaceChange_KeepsRunning()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                f.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "10.0.0.5"));
                f.Interfaces.RaiseChanged();
                f.Session.Tick();
                Assert.Equal(ServerState.Running, f.Session.State);
                Replied(f);
            }
        }

        [Fact]
        public void NoReply_AfterInterfaceLoss()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                var endpoint = f.Endpoints.Last;
                f.Interfaces.Interfaces.Clear();
                f.Session.Tick();
                endpoint.Enqueue(TestPackets.Discover(1));
                Thread.Sleep(50);
                Assert.Empty(endpoint.Sent);
            }
        }

    }

    public class LeasePersistenceTests
    {
        private static Lease OldLease()
        {
            var t = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
            return new Lease(TestPackets.MacKey(9), IPAddress.Parse("192.168.137.9"), "old", t, t.AddHours(4), false, 24);
        }

        [Fact]
        public void LeasesAreLoadedAtConstruction()
        {
            using (var f = new SessionFixture())
            {
                f.Store.Initial.Add(OldLease());
                using (var s = f.Create())
                {
                    Assert.Single(s.Leases.Snapshot());
                    Assert.False(s.StoreFailedOnLoad);
                }
            }
        }

        [Fact]
        public void LoadFailure_IsNotFatal()
        {
            using (var f = new SessionFixture())
            {
                f.Store.ThrowOnLoad = true;
                using (var s = f.Create())
                {
                    Assert.True(s.StoreFailedOnLoad);
                    Assert.Empty(s.Leases.Snapshot());
                }
            }
        }

        [Fact]
        public void LeaseChange_IsSaved_AndThrottled()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                f.Endpoints.Last.Enqueue(TestPackets.RequestSelecting(1, "192.168.137.2"));
                Assert.True(f.Endpoints.Last.WaitForSent(2));
                Assert.Equal(1, f.Store.SaveCount); // first change saved at once

                f.Endpoints.Last.Enqueue(TestPackets.Discover(2));
                f.Endpoints.Last.Enqueue(TestPackets.RequestSelecting(2, "192.168.137.3"));
                Assert.True(f.Endpoints.Last.WaitForSent(4));
                Assert.Equal(1, f.Store.SaveCount); // inside the 1 s window: deferred

                f.Clock.Advance(TimeSpan.FromSeconds(2));
                f.Session.Tick();
                Assert.Equal(2, f.Store.SaveCount);
                Assert.Equal(2, f.Store.Saved.Count);
            }
        }

        [Fact]
        public void Stop_FlushesPendingSave()
        {
            using (var f = new SessionFixture())
            {
                f.Start();
                f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                f.Endpoints.Last.Enqueue(TestPackets.RequestSelecting(1, "192.168.137.2"));
                f.Endpoints.Last.Enqueue(TestPackets.Discover(2));
                f.Endpoints.Last.Enqueue(TestPackets.RequestSelecting(2, "192.168.137.3"));
                Assert.True(f.Endpoints.Last.WaitForSent(4));
                f.Session.Stop();
                Assert.Equal(2, f.Store.Saved.Count);
            }
        }

        [Fact]
        public void SaveFailure_RaisesNotice_AndKeepsRunning()
        {
            using (var f = new SessionFixture())
            {
                f.Store.ThrowOnSave = true;
                var failed = new ManualResetEventSlim();
                f.Session.StoreFailed += (s, e) => failed.Set();
                f.Start();
                f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                f.Endpoints.Last.Enqueue(TestPackets.RequestSelecting(1, "192.168.137.2"));
                Assert.True(f.Endpoints.Last.WaitForSent(2));
                Assert.True(failed.Wait(3000));
                Assert.Equal(ServerState.Running, f.Session.State);
            }
        }

        [Fact]
        public void LeasesFromEarlierSession_ReturnSameAddress()
        {
            using (var f = new SessionFixture())
            {
                f.Store.Initial.Add(new Lease(TestPackets.MacKey(1), IPAddress.Parse("192.168.137.40"), "", f.Clock.UtcNow.AddDays(-3), f.Clock.UtcNow.AddDays(-2), false, 24));
                using (var s = f.Create())
                {
                    s.StartAsync(f.Chosen).Join();
                    f.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                    Assert.True(f.Endpoints.Last.WaitForSent(1));
                    Assert.Equal("192.168.137.40", TestPackets.Parse(f.Endpoints.Last.Sent[0]).YiAddr.ToString());
                }
            }
        }
    }
}
