using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using DhcpServer.App.Localization;
using DhcpServer.App.Settings;
using DhcpServer.App.ViewModels;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;
using DhcpServer.Tests.Fakes;
using DhcpServer.Tests.Runtime;
using Xunit;

namespace DhcpServer.Tests.ViewModels
{
    /// <summary>View-model tests run with the English string table.</summary>
    internal sealed class ViewModelFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "DhcpServerTests-" + Guid.NewGuid().ToString("N"));

        public ViewModelFixture()
        {
            LanguageService.SetLanguage("en");
            Directory.CreateDirectory(_dir);
            IniPath = Path.Combine(_dir, "DhcpServer.ini");
            Session = new SessionFixture();
            Settings = PortableSettings.Load(IniPath);
            Create();
        }

        public string IniPath { get; }

        public SessionFixture Session { get; }

        public PortableSettings Settings { get; }

        public MainViewModel Vm { get; private set; }

        public string Clipboard { get; private set; }

        public void Create()
        {
            Vm?.Dispose();
            Vm = new MainViewModel(Settings, Session.Session, Session.Interfaces, Session.Clock, null, text => Clipboard = text);
        }

        public void Select(string id)
        {
            Vm.SelectedInterface = Vm.Interfaces.First(i => i.Id == id);
        }

        public void StartAndWait()
        {
            Vm.StartStopCommand.Execute(null);
            WaitFor(() => Vm.State == ServerState.Running || Vm.State == ServerState.Failed);
        }

        public static void WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < until)
            {
                Thread.Sleep(5);
            }

            Assert.True(condition(), "condition not reached in time");
        }

        public void Dispose()
        {
            Vm.Dispose();
            Session.Dispose();
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    public class MainViewModelStartStopTests
    {
        [Fact]
        public void Initially_Stopped_NothingBound_StartDisabled_HintShown()
        {
            using (var f = new ViewModelFixture())
            {
                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.False(f.Vm.StartStopCommand.CanExecute(null));
                Assert.Equal(LanguageService.Get("Status_Stopped"), f.Vm.StatusText);
                Assert.Equal(LanguageService.Get("Hint_ChooseInterface"), f.Vm.DetailText);
                Assert.Equal(LanguageService.Get("Button_Start"), f.Vm.StartStopText);
                Assert.Empty(f.Session.Endpoints.Bound);
            }
        }

        [Fact]
        public void StartStop_Flow()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                Assert.True(f.Vm.StartStopCommand.CanExecute(null));

                f.StartAndWait();
                Assert.Equal(ServerState.Running, f.Vm.State);
                Assert.Equal(LanguageService.Format("Status_Running", "192.168.137.1"), f.Vm.StatusText);
                Assert.Equal(LanguageService.Get("Button_Stop"), f.Vm.StartStopText);
                Assert.False(f.Vm.CanChangeInterface);

                f.Vm.StartStopCommand.Execute(null);
                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.Equal(LanguageService.Get("Status_Stopped"), f.Vm.StatusText);
                Assert.True(f.Vm.CanChangeInterface);
                Assert.True(f.Session.Endpoints.Last.IsClosed);
            }
        }

        [Fact]
        public void Cancel_WhileStarting()
        {
            using (var f = new ViewModelFixture())
            {
                f.Session.Probe.Gate = new ManualResetEventSlim();
                f.Select("eth0");
                f.Vm.StartStopCommand.Execute(null);
                Assert.Equal(ServerState.Starting, f.Vm.State);
                Assert.Equal(LanguageService.Get("Button_Cancel"), f.Vm.StartStopText);
                Assert.Equal(LanguageService.Get("Status_Starting"), f.Vm.StatusText);

                f.Vm.StartStopCommand.Execute(null);
                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.Empty(f.Session.Endpoints.Bound);
            }
        }

        [Fact]
        public void Refusal_ShowsLocalizedReason_WithAddress()
        {
            using (var f = new ViewModelFixture())
            {
                f.Session.Probe.Result = ProbeResult.ServerFound(IPAddress.Parse("192.168.137.254"));
                f.Select("eth0");
                f.StartAndWait();

                Assert.Equal(ServerState.Failed, f.Vm.State);
                Assert.Equal(LanguageService.Get("Status_Failed"), f.Vm.StatusText);
                Assert.Contains("192.168.137.254", f.Vm.DetailText);
                Assert.Equal(LanguageService.Format("Reason_OtherDhcpServer", "192.168.137.254"), f.Vm.DetailText);
                Assert.True(f.Vm.CanChangeInterface);
            }
        }

        [Fact]
        public void InterfaceLoss_ShowsReason()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                f.Session.Interfaces.Interfaces.Clear();
                f.Session.Interfaces.RaiseChanged();

                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.Equal(LanguageService.Get("Reason_InterfaceLost"), f.Vm.DetailText);
            }
        }

        [Fact]
        public void PoolExhausted_ShowsNotice()
        {
            using (var f = new ViewModelFixture())
            {
                f.Session.Interfaces.Replace(TestNet.Eth(ip: "10.0.0.1", prefix: 30));
                f.Vm.Dispose();
                f.Create();
                f.Select("eth0");
                f.StartAndWait();
                var endpoint = f.Session.Endpoints.Last;
                endpoint.Enqueue(TestPackets.RequestSelecting(1, "10.0.0.2", "10.0.0.1"));
                endpoint.WaitForSent(1);
                endpoint.Enqueue(TestPackets.Discover(2));
                ViewModelFixture.WaitFor(() => f.Vm.NoticeText.Contains(LanguageService.Get("Notice_PoolExhausted")));
            }
        }

        [Fact]
        public void FirewallHint_AppearsOnlyWhenNothingArrives()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                f.Session.Clock.Advance(TimeSpan.FromSeconds(30));
                f.Vm.Tick();
                Assert.Equal(string.Empty, f.Vm.NoticeText);

                f.Session.Clock.Advance(TimeSpan.FromSeconds(40));
                f.Vm.Tick();
                Assert.Contains(LanguageService.Get("Notice_FirewallHint"), f.Vm.NoticeText);
            }
        }

        [Fact]
        public void FirewallHint_NotShown_WhenTrafficArrived()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                f.Session.Endpoints.Last.Enqueue(TestPackets.Discover(1));
                f.Session.Endpoints.Last.WaitForSent(1);
                f.Session.Clock.Advance(TimeSpan.FromMinutes(5));
                f.Vm.Tick();
                Assert.DoesNotContain(LanguageService.Get("Notice_FirewallHint"), f.Vm.NoticeText);
            }
        }

        [Fact]
        public void LeaseFileLoadFailure_ShowsNotice()
        {
            using (var f = new ViewModelFixture())
            {
                f.Session.Store.ThrowOnLoad = true;
                using (var session = f.Session.Create())
                {
                    var vm = new MainViewModel(f.Settings, session, f.Session.Interfaces, f.Session.Clock, null, null);
                    Assert.Contains(LanguageService.Get("Notice_LeaseFileNotWritable"), vm.NoticeText);
                    vm.Dispose();
                }
            }
        }
    }

    public class LeaseListTests
    {
        private static void Obtain(ViewModelFixture f, int mac, string address, string host = null)
        {
            var endpoint = f.Session.Endpoints.Last;
            var before = endpoint.Sent.Count;
            endpoint.Enqueue(TestPackets.Discover(mac, address, host));
            endpoint.Enqueue(TestPackets.RequestSelecting(mac, address, host: host));
            Assert.True(endpoint.WaitForSent(before + 2));
        }

        [Fact]
        public void NewLease_AppearsAsRow()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                Assert.False(f.Vm.HasLeases);

                Obtain(f, 1, "192.168.137.12", "esp32");

                var row = Assert.Single(f.Vm.Leases);
                Assert.True(f.Vm.HasLeases);
                Assert.Equal("192.168.137.12", row.IpAddress);
                Assert.Equal("AA:BB:CC:00:00:01", row.MacAddressText);
                Assert.Equal("esp32", row.HostName);
                Assert.Equal(LanguageService.Get("LeaseState_Active"), row.StateText);
                Assert.False(row.IsInactive);
            }
        }

        [Fact]
        public void Renewal_UpdatesInPlace_WithoutDuplicate_KeepingSelection()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                Obtain(f, 1, "192.168.137.12");
                var row = f.Vm.Leases[0];
                f.Vm.SelectedLease = row;
                var expiresBefore = row.ExpiresText;

                f.Session.Clock.Advance(TimeSpan.FromHours(2));
                var endpoint = f.Session.Endpoints.Last;
                endpoint.Enqueue(TestPackets.RequestRenew(1, "192.168.137.12"));
                Assert.True(endpoint.WaitForSent(3));

                Assert.Single(f.Vm.Leases);
                Assert.Same(row, f.Vm.Leases[0]);
                Assert.Same(row, f.Vm.SelectedLease);
                Assert.NotEqual(expiresBefore, row.ExpiresText);
            }
        }

        [Fact]
        public void Release_And_Expiry_ChangeStateText()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                Obtain(f, 1, "192.168.137.12");
                Obtain(f, 2, "192.168.137.13");

                f.Session.Endpoints.Last.Enqueue(TestPackets.Release(1, "192.168.137.12"));
                ViewModelFixture.WaitFor(() => f.Vm.Leases.First(l => l.IpAddress == "192.168.137.12").StateText == LanguageService.Get("LeaseState_Released"));

                f.Session.Clock.Advance(TimeSpan.FromHours(5));
                f.Vm.Tick();
                Assert.Equal(LanguageService.Get("LeaseState_Expired"), f.Vm.Leases.First(l => l.IpAddress == "192.168.137.13").StateText);
                Assert.True(f.Vm.Leases.All(l => l.IsInactive));
            }
        }

        [Fact]
        public void AfterStop_AllRowsAreInactive_AndStayListed()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                Obtain(f, 1, "192.168.137.12");
                f.Vm.StartStopCommand.Execute(null);

                var row = Assert.Single(f.Vm.Leases);
                Assert.Equal(LanguageService.Get("LeaseState_Inactive"), row.StateText);
                Assert.True(row.IsInactive);
            }
        }

        [Fact]
        public void Rows_AreOrderedNumericallyByIp()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                Obtain(f, 1, "192.168.137.100");
                Obtain(f, 2, "192.168.137.20");
                Obtain(f, 3, "192.168.137.3");

                Assert.Equal(new[] { "192.168.137.3", "192.168.137.20", "192.168.137.100" }, f.Vm.Leases.Select(l => l.IpAddress).ToArray());
            }
        }

        [Fact]
        public void LeasesFromEarlierSession_AreShownAtStartup_AsInactive()
        {
            using (var f = new ViewModelFixture())
            {
                var t = f.Session.Clock.UtcNow;
                f.Session.Store.Initial.Add(new DhcpServer.Core.Leases.Lease(
                    TestPackets.MacKey(9), IPAddress.Parse("192.168.137.9"), "old", t.AddHours(-1), t.AddHours(3), false, 24));
                using (var session = f.Session.Create())
                {
                    var vm = new MainViewModel(f.Settings, session, f.Session.Interfaces, f.Session.Clock, null, null);
                    var row = Assert.Single(vm.Leases);
                    Assert.Equal(LanguageService.Get("LeaseState_Inactive"), row.StateText);
                    vm.Dispose();
                }
            }
        }

        [Fact]
        public void CopyCommands_PutExpectedTextOnClipboard()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                Obtain(f, 1, "192.168.137.12");
                var row = f.Vm.Leases[0];

                row.CopyIpCommand.Execute(null);
                Assert.Equal("192.168.137.12", f.Clipboard);
                row.CopyMacCommand.Execute(null);
                Assert.Equal("AA:BB:CC:00:00:01", f.Clipboard);

                Assert.False(f.Vm.CopyRowCommand.CanExecute(null));
                f.Vm.SelectedLease = row;
                f.Vm.CopyRowCommand.Execute(null);
                Assert.Equal("192.168.137.12\tAA:BB:CC:00:00:01", f.Clipboard);
            }
        }
    }

    public class InterfaceListTests
    {
        [Fact]
        public void EveryAdapter_IsListed_WithNameAddressAndStatus_LoopbackHidden()
        {
            using (var f = new ViewModelFixture())
            {
                f.Session.Interfaces.Interfaces.Add(new InterfaceInfo("lo", "Loopback", InterfaceKind.Loopback, true, IPAddress.Loopback, 8));
                f.Session.Interfaces.Interfaces.Add(TestNet.Eth("eth1", ip: null, up: false));
                f.Vm.Dispose();
                f.Create();

                Assert.Equal(2, f.Vm.Interfaces.Count);
                var eth0 = f.Vm.Interfaces.First(i => i.Id == "eth0");
                Assert.Equal("Ethernet eth0 — 192.168.137.1 — " + LanguageService.Get("Interface_Status_Up"), eth0.DisplayName);
                var eth1 = f.Vm.Interfaces.First(i => i.Id == "eth1");
                Assert.Equal("Ethernet eth1 — " + LanguageService.Get("Interface_NoAddress") + " — " + LanguageService.Get("Interface_Status_Down"), eth1.DisplayName);
            }
        }

        [Fact]
        public void Unusable_AreFlagged_WithReason_AndCannotBeSelected()
        {
            using (var f = new ViewModelFixture())
            {
                f.Session.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "192.168.137.50"));
                f.Session.Interfaces.Interfaces.Add(TestNet.Eth("eth2", ip: null, up: false));
                f.Vm.Dispose();
                f.Create();

                var overlapping = f.Vm.Interfaces.First(i => i.Id == "eth1");
                Assert.False(overlapping.IsSelectable);
                Assert.Equal(LanguageService.Get("Reason_SubnetOverlap"), overlapping.UnavailableReason);

                var down = f.Vm.Interfaces.First(i => i.Id == "eth2");
                Assert.Equal(LanguageService.Get("Reason_Down"), down.UnavailableReason);

                f.Vm.SelectedInterface = down;
                Assert.Null(f.Vm.SelectedInterface);
                Assert.False(f.Vm.StartStopCommand.CanExecute(null));
            }
        }

        [Fact]
        public void ListRefreshes_OnProviderChange_WhileStopped()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                var selected = f.Vm.SelectedInterface;
                f.Session.Interfaces.Interfaces.Add(TestNet.Eth("eth9", "10.9.0.1"));
                f.Session.Interfaces.RaiseChanged();

                Assert.Equal(2, f.Vm.Interfaces.Count);
                Assert.Same(selected, f.Vm.SelectedInterface);

                f.Session.Interfaces.Interfaces.RemoveAll(i => i.Id == "eth0");
                f.Session.Interfaces.RaiseChanged();
                Assert.Null(f.Vm.SelectedInterface);
                Assert.Single(f.Vm.Interfaces);
            }
        }

        [Fact]
        public void LastInterface_IsRestored_ButNeverAutoStarted()
        {
            using (var f = new ViewModelFixture())
            {
                f.Select("eth0");
                f.StartAndWait();
                f.Vm.StartStopCommand.Execute(null);
                Assert.True(f.Settings.TrySave());

                var again = PortableSettings.Load(f.IniPath);
                Assert.Equal("eth0", again.LastInterface);

                var bindsBefore = f.Session.Endpoints.Bound.Count;
                using (var session = f.Session.Create())
                {
                    var vm = new MainViewModel(again, session, f.Session.Interfaces, f.Session.Clock, null, null);
                    Assert.Equal("eth0", vm.SelectedInterface.Id);
                    Assert.Equal(ServerState.Stopped, vm.State);
                    Assert.Equal(bindsBefore, f.Session.Endpoints.Bound.Count);
                    vm.Dispose();
                }
            }
        }

        [Fact]
        public void LastInterface_Unusable_IsNotPreselected()
        {
            using (var f = new ViewModelFixture())
            {
                f.Settings.LastInterface = "eth0";
                f.Session.Interfaces.Replace(TestNet.Eth(up: false));
                f.Create();
                Assert.Null(f.Vm.SelectedInterface);
            }
        }

        [Fact]
        public void LanguageSwitch_RefreshesTexts()
        {
            using (var f = new ViewModelFixture())
            {
                var english = f.Vm.StatusText;
                try
                {
                    LanguageService.SetLanguage("de");
                    Assert.NotEqual(english, f.Vm.StatusText);
                    Assert.Equal(LanguageService.Get("Status_Stopped"), f.Vm.StatusText);
                }
                finally
                {
                    LanguageService.SetLanguage("en");
                }
            }
        }
    }
}
