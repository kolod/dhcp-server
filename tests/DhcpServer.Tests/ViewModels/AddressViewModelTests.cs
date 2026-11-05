using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using DhcpServer.App.Localization;
using DhcpServer.App.Settings;
using DhcpServer.App.ViewModels;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;
using DhcpServer.Tests.Fakes;
using DhcpServer.Tests.Runtime;
using Xunit;

namespace DhcpServer.Tests.ViewModels
{
    internal sealed class FakeUserDialogs : IUserDialogs
    {
        public bool ConfirmAnswer { get; set; } = true;

        public bool DiscardAnswer { get; set; } = true;

        public List<ChangePreview> Previews { get; } = new List<ChangePreview>();

        public int DiscardAsked { get; private set; }

        /// <summary>Runs inside ConfirmChanges, e.g. to prove that nothing has changed yet.</summary>
        public Action OnConfirm { get; set; }

        public bool ConfirmChanges(ChangePreview preview, string adapterName)
        {
            Previews.Add(preview);
            OnConfirm?.Invoke();
            return ConfirmAnswer;
        }

        public bool ConfirmDiscard()
        {
            DiscardAsked++;
            return DiscardAnswer;
        }
    }

    internal sealed class AddressVmFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "DhcpServerAddrVm-" + Guid.NewGuid().ToString("N"));

        /// <summary>An adapter that has only an automatic link-local address: it needs a fixed one.</summary>
        public static AdapterConfig LinkLocalAutomatic()
        {
            return new AdapterConfig("eth0", 7, new[] { new AdapterAddress(IPAddress.Parse("169.254.9.9"), 16) }, null,
                new IPAddress[0], AddressMode.Automatic, DnsMode.Automatic);
        }

        public AddressVmFixture(AdapterConfig original = null, Action<AddressFixture> beforeViewModel = null)
        {
            LanguageService.SetLanguage("en");
            Directory.CreateDirectory(_dir);
            Core = new AddressFixture(original ?? LinkLocalAutomatic());
            beforeViewModel?.Invoke(Core);
            Settings = PortableSettings.Load(Path.Combine(_dir, "DhcpServer.ini"));
            Vm = new MainViewModel(Settings, Core.Session, Core.Interfaces, Core.Clock, null, text => Clipboard = text,
                Core.Service, Dialogs, Core.Recovery);
        }

        public AddressFixture Core { get; }

        public FakeUserDialogs Dialogs { get; } = new FakeUserDialogs();

        public PortableSettings Settings { get; }

        public MainViewModel Vm { get; }

        public string Clipboard { get; private set; }

        public InterfaceItemViewModel Select(string id = "eth0")
        {
            var item = Vm.Interfaces.First(i => i.Id == id);
            Vm.SelectedInterface = item;
            return item;
        }

        public void StartAndWait()
        {
            Vm.StartStopCommand.Execute(null);
            ViewModelFixture.WaitFor(() => Vm.State == ServerState.Running || Vm.State == ServerState.Failed);
        }

        public void Dispose()
        {
            Vm.Dispose();
            Core.Dispose();
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Collection("UiLanguage")]
    public class AddressSettingsViewModelTests
    {
        [Fact]
        public void AdapterThatNeedsAnAddress_LocksTheCheckbox_AndPrefillsASuggestion()
        {
            using (var f = new AddressVmFixture())
            {
                var item = f.Select();

                Assert.True(item.NeedsAddress);
                Assert.True(item.IsSelectable);
                Assert.Contains(LanguageService.Get("Interface_NeedsAddress"), item.DisplayName);
                Assert.True(f.Vm.Address.IsLocked);
                Assert.True(f.Vm.Address.UseFixedAddress);
                Assert.False(f.Vm.Address.CanToggle);
                Assert.Equal("192.168.137.1", f.Vm.Address.Address);
                Assert.Equal("255.255.255.0", f.Vm.Address.Mask);
                Assert.Equal(string.Empty, f.Vm.Address.Gateway);
                Assert.False(f.Vm.Address.HasErrors);
                Assert.True(f.Vm.StartStopCommand.CanExecute(null));

                f.Vm.Address.UseFixedAddress = false; // locked: cannot be turned off
                Assert.True(f.Vm.Address.UseFixedAddress);
            }
        }

        [Fact]
        public void UsableAdapter_ShowsCurrentValuesReadOnly_AndCanSwitchToFixed()
        {
            using (var f = new AddressVmFixture(AddressingTestData.Static("eth0", "192.168.50.7", 24, "192.168.50.1")))
            {
                f.Select();

                Assert.False(f.Vm.Address.UseFixedAddress);
                Assert.True(f.Vm.Address.CanToggle);
                Assert.True(f.Vm.Address.FieldsReadOnly);
                Assert.Equal("192.168.50.7", f.Vm.Address.Address);
                Assert.Equal("255.255.255.0", f.Vm.Address.Mask);
                Assert.Equal("192.168.50.1", f.Vm.Address.Gateway);

                f.Vm.Address.UseFixedAddress = true;
                Assert.False(f.Vm.Address.FieldsReadOnly);
                Assert.Equal("192.168.137.1", f.Vm.Address.Address);
                Assert.Equal(string.Empty, f.Vm.Address.Gateway);
            }
        }

        [Fact]
        public void TypedValues_AreNeverOverwritten_ButChangingAdapterResuggests()
        {
            using (var f = new AddressVmFixture())
            {
                f.Core.Interfaces.Interfaces.Add(TestNet.Eth("eth9", ip: null));
                f.Core.Interfaces.RaiseChanged();
                f.Select();
                f.Vm.Address.Address = "10.55.0.1";
                f.Core.Interfaces.RaiseChanged();
                Assert.Equal("10.55.0.1", f.Vm.Address.Address);

                f.Select("eth9");
                Assert.Equal("192.168.137.1", f.Vm.Address.Address);
            }
        }

        [Fact]
        public void SuggestionAvoidsSubnetsOfOtherActiveInterfaces()
        {
            using (var f = new AddressVmFixture())
            {
                f.Core.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "192.168.137.20"));
                f.Core.Interfaces.RaiseChanged();
                f.Select();
                Assert.Equal("192.168.138.1", f.Vm.Address.Address);
            }
        }

        [Theory]
        [InlineData("Address", "bad", "Field_Malformed")]
        [InlineData("Address", "", "Field_Required")]
        [InlineData("Address", "127.0.0.1", "Field_NotUnicast")]
        [InlineData("Address", "169.254.1.1", "Field_LinkLocal")]
        [InlineData("Address", "192.168.137.0", "Field_NetworkOrBroadcast")]
        [InlineData("Mask", "255.0.255.0", "Field_MaskInvalid")]
        [InlineData("Mask", "255.255.255.255", "Field_MaskSize")]
        [InlineData("Gateway", "192.168.200.1", "Field_GatewayOutsideSubnet")]
        [InlineData("Dns1", "x", "Field_Malformed")]
        public void InvalidInput_ShowsInlineMessage_AndBlocksStart(string field, string value, string key)
        {
            using (var f = new AddressVmFixture())
            {
                f.Select();
                var vm = f.Vm.Address;
                switch (field)
                {
                    case "Address": vm.Address = value; break;
                    case "Mask": vm.Mask = value; break;
                    case "Gateway": vm.Gateway = value; break;
                    default: vm.Dns1 = value; break;
                }

                var shown = field == "Address" ? vm.AddressError : field == "Mask" ? vm.MaskError : field == "Gateway" ? vm.GatewayError : vm.Dns1Error;
                Assert.Equal(LanguageService.Get(key), shown);
                Assert.True(vm.HasErrors);
                Assert.StartsWith("⚠", vm.FirstError);
                Assert.False(f.Vm.StartStopCommand.CanExecute(null));
            }
        }

        [Fact]
        public void DuplicateDns_AndOverlap_AreReported()
        {
            using (var f = new AddressVmFixture())
            {
                f.Core.Interfaces.Interfaces.Add(TestNet.Eth("eth1", "10.77.0.5"));
                f.Core.Interfaces.RaiseChanged();
                f.Select();

                f.Vm.Address.Dns1 = "8.8.8.8";
                f.Vm.Address.Dns2 = "8.8.8.8";
                Assert.Equal(LanguageService.Get("Field_DnsDuplicate"), f.Vm.Address.Dns2Error);

                f.Vm.Address.Dns2 = "1.1.1.1";
                f.Vm.Address.Address = "10.77.0.9";
                f.Vm.Address.Mask = "255.255.255.0";
                Assert.Equal(LanguageService.Get("Field_SubnetOverlap"), f.Vm.Address.AddressError);
            }
        }

        [Fact]
        public void ErrorTexts_FollowTheLanguage()
        {
            using (var f = new AddressVmFixture())
            {
                f.Select();
                f.Vm.Address.Address = "bad";
                var english = f.Vm.Address.AddressError;
                try
                {
                    LanguageService.SetLanguage("de");
                    Assert.NotEqual(english, f.Vm.Address.AddressError);
                    Assert.Equal(LanguageService.Get("Field_Malformed"), f.Vm.Address.AddressError);
                }
                finally
                {
                    LanguageService.SetLanguage("en");
                }
            }
        }

        [Fact]
        public void DownAdapter_CannotStart_AndShowsTheReason()
        {
            using (var f = new AddressVmFixture(null, c => c.Interfaces.Replace(TestNet.Eth("eth0", ip: null, up: false))))
            {
                f.Select();
                Assert.False(f.Vm.StartStopCommand.CanExecute(null));
                Assert.Equal(LanguageService.Get("Reason_Down"), f.Vm.DetailText);
            }
        }

        [Fact]
        public void TunnelAdapter_CannotBeChosen()
        {
            using (var f = new AddressVmFixture(null, c => c.Interfaces.Interfaces.Add(new InterfaceInfo("tun", "VPN", InterfaceKind.Tunnel, true, IPAddress.Parse("10.8.0.2"), 24))))
            {
                var tunnel = f.Vm.Interfaces.First(i => i.Id == "tun");
                Assert.False(tunnel.IsSelectable);
                f.Vm.SelectedInterface = tunnel;
                Assert.Null(f.Vm.SelectedInterface);
            }
        }
    }

    [Collection("UiLanguage")]
    public class AddressStartFlowTests
    {
        [Fact]
        public void Preview_IsShown_AndNothingChangesBeforeConsent()
        {
            using (var f = new AddressVmFixture())
            {
                var callsAtConsent = -1;
                var recordAtConsent = true;
                f.Dialogs.OnConfirm = () =>
                {
                    callsAtConsent = f.Core.Service.Calls.Count;
                    recordAtConsent = f.Core.Store.Record != null;
                };
                f.Select();

                f.StartAndWait();

                Assert.Equal(ServerState.Running, f.Vm.State);
                Assert.Equal(0, callsAtConsent);
                Assert.False(recordAtConsent);
                var preview = Assert.Single(f.Dialogs.Previews);
                Assert.Equal("169.254.9.9", preview.Rows.Single(r => r.LabelKey == "Address_Address").Current.Text);
                Assert.Equal("192.168.137.1", preview.Rows.Single(r => r.LabelKey == "Address_Address").New.Text);
                Assert.True(AdapterConfig.Equivalent(AdapterConfig.FromSettings("eth0", 7, AddressingTestData.Settings("192.168.137.1", 24, null)), f.Core.Current));
                Assert.Contains(LanguageService.Get("Notice_SettingsApplied"), f.Vm.NoticeText);
                Assert.False(f.Vm.CanChangeInterface);
            }
        }

        [Fact]
        public void Cancel_ChangesNothing_AndDoesNotStart()
        {
            using (var f = new AddressVmFixture())
            {
                f.Dialogs.ConfirmAnswer = false;
                f.Select();

                f.Vm.StartStopCommand.Execute(null);
                Thread.Sleep(50);

                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.Single(f.Dialogs.Previews);
                Assert.Empty(f.Core.Service.Calls);
                Assert.Equal(0, f.Core.Store.SaveCount);
                Assert.Empty(f.Core.Endpoints.Bound);
            }
        }

        [Fact]
        public void KeepingAUsableAddress_StartsWithoutPreviewOrChanges()
        {
            using (var f = new AddressVmFixture(AddressingTestData.Static("eth0", "192.168.137.1", 24, null)))
            {
                f.Select();
                f.StartAndWait();

                Assert.Equal(ServerState.Running, f.Vm.State);
                Assert.Empty(f.Dialogs.Previews);
                Assert.Empty(f.Core.Service.Calls);
                Assert.Equal(0, f.Core.Store.SaveCount);
            }
        }

        [Fact]
        public void Stop_RestoresAndSaysSo()
        {
            using (var f = new AddressVmFixture())
            {
                f.Select();
                f.StartAndWait();
                f.Vm.StartStopCommand.Execute(null);

                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.True(AdapterConfig.Equivalent(f.Core.Original, f.Core.Current));
                Assert.Null(f.Core.Store.Record);
                Assert.Contains(LanguageService.Get("Notice_SettingsRestored"), f.Vm.NoticeText);
                Assert.DoesNotContain(LanguageService.Get("Notice_SettingsApplied"), f.Vm.NoticeText);
            }
        }

        [Fact]
        public void UnreadableAdapter_RefusesWithoutPreview_OrChange()
        {
            using (var f = new AddressVmFixture())
            {
                f.Select();
                f.Core.Service.Adapters.Clear();

                f.Vm.StartStopCommand.Execute(null);

                Assert.Empty(f.Dialogs.Previews);
                Assert.Empty(f.Core.Service.Calls);
                Assert.Equal(ServerState.Stopped, f.Vm.State);
                Assert.Equal(LanguageService.Format("Reason_AddressApplyFailed", LanguageService.Get("Step_Capture")), f.Vm.DetailText);
            }
        }

        [Fact]
        public void FailedApply_ShowsStepName_AndOldSettingsAreBack()
        {
            using (var f = new AddressVmFixture())
            {
                f.Core.Service.FailOn(ApplyStep.SetAddress);
                f.Select();
                f.StartAndWait();

                Assert.Equal(ServerState.Failed, f.Vm.State);
                Assert.Equal(LanguageService.Format("Reason_AddressApplyFailed", LanguageService.Get("Step_SetAddress")), f.Vm.DetailText);
                Assert.True(AdapterConfig.Equivalent(f.Core.Original, f.Core.Current));
                Assert.True(f.Vm.CanChangeInterface);
            }
        }

        [Fact]
        public void FailedRestore_ShowsStepAndPreviousSettings()
        {
            using (var f = new AddressVmFixture(AddressingTestData.Static("eth0", "192.168.50.7", 24, "192.168.50.1", "8.8.8.8")))
            {
                f.Select();
                f.Vm.Address.UseFixedAddress = true;
                f.StartAndWait();
                f.Core.Service.FailOn(ApplyStep.SetAddress, 2);

                f.Vm.StartStopCommand.Execute(null);

                var text = f.Vm.DetailText;
                Assert.Contains(LanguageService.Get("Step_SetAddress"), text);
                Assert.Contains("192.168.50.7/24", text);
                Assert.Contains("192.168.50.1", text);
                Assert.Contains("8.8.8.8", text);
            }
        }

        [Fact]
        public void RecordNotWritable_IsExplained()
        {
            using (var f = new AddressVmFixture())
            {
                f.Core.Store.ThrowOnSave = true;
                f.Select();
                f.StartAndWait();
                Assert.Equal(LanguageService.Get("Reason_RecordNotWritable"), f.Vm.DetailText);
                Assert.Empty(f.Core.Service.Calls);
            }
        }
    }

    [Collection("UiLanguage")]
    public class RecoveryViewModelTests
    {
        private static void Crash(AddressFixture c)
        {
            Assert.True(c.Configurator.Apply(c.Chosen, AddressingTestData.Settings()).Succeeded);
        }

        [Fact]
        public void NoRecord_NoBanner()
        {
            using (var f = new AddressVmFixture())
            {
                Assert.False(f.Vm.RecoveryVisible);
                Assert.Equal(string.Empty, f.Vm.RecoveryText);
            }
        }

        [Fact]
        public void RecordAtStart_IsRestoredFirst_AndReported()
        {
            using (var f = new AddressVmFixture(AddressingTestData.Static("eth0", "192.168.50.7", 24, "192.168.50.1"), Crash))
            {
                Assert.False(f.Vm.RecoveryVisible);
                Assert.True(AdapterConfig.Equivalent(f.Core.Original, f.Core.Current));
                Assert.Null(f.Core.Store.Record);
                Assert.Contains(LanguageService.Get("Notice_SettingsRestored"), f.Vm.NoticeText);
            }
        }

        [Fact]
        public void FailedRestore_ShowsBanner_BlocksStart_AndRetryResolvesIt()
        {
            using (var f = new AddressVmFixture(AddressingTestData.Static("eth0", "192.168.50.7", 24, "192.168.50.1"), c =>
            {
                Crash(c);
                c.Service.FailOn(ApplyStep.SetAddress, 2);
            }))
            {
                Assert.True(f.Vm.RecoveryVisible);
                Assert.Contains("Ethernet eth0", f.Vm.RecoveryText);
                Assert.Contains("192.168.50.7/24", f.Vm.RecoveryText);
                Assert.Contains(LanguageService.Get("Step_SetAddress"), f.Vm.RecoveryText);
                f.Select();
                Assert.False(f.Vm.StartStopCommand.CanExecute(null));
                Assert.True(f.Vm.RetryRecoveryCommand.CanExecute(null));

                f.Vm.RetryRecoveryCommand.Execute(null);

                Assert.False(f.Vm.RecoveryVisible);
                Assert.True(f.Vm.StartStopCommand.CanExecute(null));
                Assert.True(AdapterConfig.Equivalent(f.Core.Original, f.Core.Current));
            }
        }

        [Fact]
        public void Discard_AsksFirst_AndOnlyThenDeletesTheRecord()
        {
            using (var f = new AddressVmFixture(null, c =>
            {
                Crash(c);
                c.Service.Adapters.Clear();
            }))
            {
                Assert.True(f.Vm.RecoveryVisible);
                Assert.Contains(LanguageService.Get("Notice_AdapterMissing"), f.Vm.RecoveryText);

                f.Dialogs.DiscardAnswer = false;
                f.Vm.DiscardRecoveryCommand.Execute(null);
                Assert.Equal(1, f.Dialogs.DiscardAsked);
                Assert.True(f.Vm.RecoveryVisible);
                Assert.NotNull(f.Core.Store.Record);

                f.Dialogs.DiscardAnswer = true;
                f.Vm.DiscardRecoveryCommand.Execute(null);
                Assert.False(f.Vm.RecoveryVisible);
                Assert.Null(f.Core.Store.Record);
            }
        }

        [Fact]
        public void CorruptRecord_OnlyDiscardIsOffered()
        {
            using (var f = new AddressVmFixture(null, c => c.Store.Corrupt = true))
            {
                Assert.True(f.Vm.RecoveryVisible);
                Assert.True(f.Vm.RecoveryCorrupt);
                Assert.Equal(LanguageService.Get("Recovery_Corrupt"), f.Vm.RecoveryText);
                Assert.False(f.Vm.RetryRecoveryCommand.CanExecute(null));
                Assert.True(f.Vm.DiscardRecoveryCommand.CanExecute(null));
            }
        }

        [Fact]
        public void AdapterChangedByUser_IsLeftAlone_AndStartAllowed()
        {
            using (var f = new AddressVmFixture(null, c =>
            {
                Crash(c);
                c.Service.Set(AddressingTestData.Static("eth0", "10.99.99.9", 24, null));
            }))
            {
                Assert.False(f.Vm.RecoveryVisible);
                Assert.Contains(LanguageService.Get("Notice_SettingsLeftUnchanged"), f.Vm.NoticeText);
                f.Select();
                Assert.True(f.Vm.StartStopCommand.CanExecute(null));
            }
        }
    }

    [Collection("UiLanguage")]
    public class FailureTextTests
    {
        public FailureTextTests()
        {
            LanguageService.SetLanguage("en");
        }

        [Fact]
        public void EveryApplyStep_HasAStepText()
        {
            foreach (ApplyStep step in Enum.GetValues(typeof(ApplyStep)))
            {
                Assert.DoesNotContain("[", FailureText.Step(step.ToString()));
            }
        }

        [Fact]
        public void EveryFailureKind_HasAReasonText()
        {
            foreach (FailureKind kind in Enum.GetValues(typeof(FailureKind)))
            {
                var text = FailureText.Format(new FailureReason(kind, "x"));
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("[Reason_", text);
            }
        }

        [Fact]
        public void Describe_AutomaticStaticAndMulti()
        {
            Assert.Equal(LanguageService.Get("Confirm_Automatic"), FailureText.Describe(AddressingTestData.Dhcp()));

            var text = FailureText.Describe(AddressingTestData.StaticMulti());
            Assert.Contains("10.20.30.40/24, 172.16.5.5/16", text);
            Assert.Contains("10.20.30.1", text);
            Assert.Contains("8.8.8.8, 1.1.1.1", text);

            var noGatewayNoDns = FailureText.Describe(AddressingTestData.Static("eth0", "10.0.0.5", 24, null));
            Assert.Contains(LanguageService.Get("Confirm_None"), noGatewayNoDns);
            Assert.Equal(string.Empty, FailureText.Describe(null));
        }

        [Fact]
        public void ConfirmChangesViewModel_TranslatesMarkersAndFlagsChangedRows()
        {
            var preview = ChangePreview.Create(AddressingTestData.Dhcp(), AddressingTestData.Settings("192.168.137.1", 24, null));
            var vm = new ConfirmChangesViewModel(preview, "Ethernet 5");

            Assert.Equal(LanguageService.Format("Confirm_Intro", "Ethernet 5"), vm.Intro);
            var mode = vm.Rows.Single(r => r.Label == LanguageService.Get("Confirm_Mode"));
            Assert.Equal(LanguageService.Get("Confirm_Automatic"), mode.Current);
            Assert.Equal(LanguageService.Get("Confirm_Static"), mode.New);
            Assert.True(mode.Changed);
            var gateway = vm.Rows.Single(r => r.Label == LanguageService.Get("Address_Gateway"));
            Assert.Equal(LanguageService.Get("Confirm_None"), gateway.New);
        }
    }
}
