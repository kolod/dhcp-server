using System;
using System.Linq;
using System.Net;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Network;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Addressing
{
    public class AdapterConfigTests
    {
        [Fact]
        public void Static_Equivalent_IgnoresAddressOrder()
        {
            var a = AddressingTestData.StaticMulti();
            var b = new AdapterConfig(a.AdapterId, 99, a.Addresses.Reverse(), a.Gateway, a.DnsServers, AddressMode.Static, DnsMode.Static);
            Assert.True(AdapterConfig.Equivalent(a, b));
        }

        [Fact]
        public void Static_DifferentAddressPrefixGatewayOrDns_NotEquivalent()
        {
            var a = AddressingTestData.Static(dns: new[] { "8.8.8.8", "1.1.1.1" });
            Assert.False(AdapterConfig.Equivalent(a, AddressingTestData.Static(ip: "10.20.30.41", dns: new[] { "8.8.8.8", "1.1.1.1" })));
            Assert.False(AdapterConfig.Equivalent(a, AddressingTestData.Static(prefix: 16, dns: new[] { "8.8.8.8", "1.1.1.1" })));
            Assert.False(AdapterConfig.Equivalent(a, AddressingTestData.Static(gateway: null, dns: new[] { "8.8.8.8", "1.1.1.1" })));
            Assert.False(AdapterConfig.Equivalent(a, AddressingTestData.Static(dns: new[] { "1.1.1.1", "8.8.8.8" })));
        }

        [Fact]
        public void Dhcp_IgnoresLeasedAddress()
        {
            var a = AddressingTestData.Dhcp();
            var b = new AdapterConfig(a.AdapterId, 1, new[] { new AdapterAddress(IPAddress.Parse("10.99.0.2"), 24) }, null, new IPAddress[0], AddressMode.Automatic, DnsMode.Automatic);
            Assert.True(AdapterConfig.Equivalent(a, b));
        }

        [Fact]
        public void ModeMismatch_NotEquivalent()
        {
            Assert.False(AdapterConfig.Equivalent(AddressingTestData.Dhcp(), AddressingTestData.Static()));
            var staticDns = new AdapterConfig("eth0", 1, AddressingTestData.Dhcp().Addresses, null, new[] { IPAddress.Parse("8.8.8.8") }, AddressMode.Automatic, DnsMode.Static);
            Assert.False(AdapterConfig.Equivalent(AddressingTestData.Dhcp(), staticDns));
        }

        [Fact]
        public void Null_NotEquivalent()
        {
            Assert.False(AdapterConfig.Equivalent(null, AddressingTestData.Static()));
        }

        [Fact]
        public void FromSettings_BuildsStaticConfig_WithStaticEmptyDnsWhenNone()
        {
            var config = AdapterConfig.FromSettings("eth0", 5, AddressingTestData.Settings("192.168.137.1", 24, null));
            Assert.Equal(AddressMode.Static, config.AddressMode);
            Assert.Equal(DnsMode.Static, config.DnsMode);
            Assert.Empty(config.DnsServers);
            Assert.Null(config.Gateway);
            Assert.Single(config.Addresses);
            Assert.Equal(5, config.InterfaceIndex);

            var full = AdapterConfig.FromSettings("eth0", 5, AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254", "8.8.8.8", "1.1.1.1"));
            Assert.Equal("192.168.137.254", full.Gateway.ToString());
            Assert.Equal(2, full.DnsServers.Count);
        }
    }

    public class AddressSettingsValidatorTests
    {
        private static ValidationResult Validate(string address, string mask, string gateway = "", string dns1 = "", string dns2 = "", params InterfaceInfo[] others)
        {
            var all = others.Concat(new[] { TestNet.Eth("eth0", "10.0.0.5", 8) });
            return AddressSettingsValidator.Validate(
                new AddressFieldInput { Address = address, Mask = mask, Gateway = gateway, Dns1 = dns1, Dns2 = dns2 },
                "eth0",
                all);
        }

        private static void AssertError(ValidationResult result, AddressField field, FieldErrorKind kind)
        {
            Assert.Contains(result.Errors, e => e.Field == field && e.Kind == kind);
            Assert.False(result.IsValid);
            Assert.Null(result.Settings);
        }

        [Fact]
        public void MinimalAndFullSettings_AreValid()
        {
            var minimal = Validate("192.168.137.1", "255.255.255.0");
            Assert.True(minimal.IsValid);
            Assert.Equal(24, minimal.Settings.PrefixLength);
            Assert.Null(minimal.Settings.Gateway);
            Assert.Empty(minimal.Settings.DnsServers);

            var full = Validate("192.168.137.1", "255.255.255.0", "192.168.137.254", "8.8.8.8", "1.1.1.1");
            Assert.True(full.IsValid);
            Assert.Equal("192.168.137.254", full.Settings.Gateway.ToString());
            Assert.Equal(2, full.Settings.DnsServers.Count);
        }

        [Fact]
        public void Required()
        {
            var r = Validate("", " ");
            AssertError(r, AddressField.Address, FieldErrorKind.Required);
            AssertError(r, AddressField.Mask, FieldErrorKind.Required);
        }

        [Theory]
        [InlineData("192.168.1")]
        [InlineData("192.168.1.300")]
        [InlineData("abc")]
        [InlineData("1.2.3.4.5")]
        [InlineData("192.168.01.x")]
        public void Malformed(string text)
        {
            AssertError(Validate(text, "255.255.255.0"), AddressField.Address, FieldErrorKind.Malformed);
            AssertError(Validate("192.168.1.1", text), AddressField.Mask, FieldErrorKind.Malformed);
        }

        [Theory]
        [InlineData("0.1.2.3")]
        [InlineData("127.0.0.1")]
        [InlineData("224.0.0.5")]
        [InlineData("255.255.255.255")]
        public void NotUnicast(string text)
        {
            AssertError(Validate(text, "255.255.255.0"), AddressField.Address, FieldErrorKind.NotUnicast);
        }

        [Fact]
        public void LinkLocal()
        {
            AssertError(Validate("169.254.5.5", "255.255.0.0"), AddressField.Address, FieldErrorKind.LinkLocal);
        }

        [Theory]
        [InlineData("255.0.255.0")]
        [InlineData("255.255.255.1")]
        public void MaskInvalid(string mask)
        {
            AssertError(Validate("192.168.1.1", mask), AddressField.Mask, FieldErrorKind.MaskInvalid);
        }

        [Theory]
        [InlineData("254.0.0.0")]
        [InlineData("255.255.255.254")]
        [InlineData("255.255.255.255")]
        [InlineData("0.0.0.0")]
        public void MaskSize(string mask)
        {
            AssertError(Validate("192.168.1.1", mask), AddressField.Mask, FieldErrorKind.MaskSize);
        }

        [Fact]
        public void Slash8AndSlash30_AreAllowed()
        {
            Assert.True(Validate("11.1.1.1", "255.0.0.0").IsValid);
            Assert.True(Validate("192.168.1.1", "255.255.255.252").IsValid);
        }

        [Theory]
        [InlineData("192.168.1.0")]
        [InlineData("192.168.1.255")]
        public void NetworkOrBroadcast(string address)
        {
            AssertError(Validate(address, "255.255.255.0"), AddressField.Address, FieldErrorKind.NetworkOrBroadcast);
        }

        [Theory]
        [InlineData("192.168.2.1")]
        [InlineData("192.168.1.0")]
        [InlineData("192.168.1.255")]
        [InlineData("192.168.1.1")]
        public void GatewayMustBeAnotherHostInsideTheSubnet(string gateway)
        {
            AssertError(Validate("192.168.1.1", "255.255.255.0", gateway), AddressField.Gateway, FieldErrorKind.GatewayOutsideSubnet);
        }

        [Fact]
        public void GatewayMalformed()
        {
            AssertError(Validate("192.168.1.1", "255.255.255.0", "x"), AddressField.Gateway, FieldErrorKind.Malformed);
        }

        [Fact]
        public void Dns_MalformedAndDuplicate()
        {
            AssertError(Validate("192.168.1.1", "255.255.255.0", "", "nope"), AddressField.Dns1, FieldErrorKind.Malformed);
            AssertError(Validate("192.168.1.1", "255.255.255.0", "", "8.8.8.8", "8.8.8.8"), AddressField.Dns2, FieldErrorKind.DnsDuplicate);
            AssertError(Validate("192.168.1.1", "255.255.255.0", "", "127.0.0.1"), AddressField.Dns1, FieldErrorKind.NotUnicast);
        }

        [Fact]
        public void OnlySecondDns_IsAcceptedAsFirst()
        {
            var r = Validate("192.168.1.1", "255.255.255.0", "", "", "8.8.8.8");
            Assert.True(r.IsValid);
            Assert.Equal("8.8.8.8", r.Settings.DnsServers.Single().ToString());
        }

        [Fact]
        public void Overlap_JudgedAgainstProposedSubnet_OfOtherActiveInterfaces()
        {
            var other = TestNet.Eth("eth1", "192.168.1.77");
            AssertError(Validate("192.168.1.1", "255.255.255.0", "", "", "", other), AddressField.Address, FieldErrorKind.SubnetOverlap);

            // a wider proposed mask overlaps too
            AssertError(Validate("192.168.9.1", "255.255.0.0", "", "", "", other), AddressField.Address, FieldErrorKind.SubnetOverlap);
        }

        [Fact]
        public void Overlap_IgnoresChosenAdapter_DownAndLoopback()
        {
            var down = TestNet.Eth("eth1", "192.168.1.77", up: false);
            var loop = new InterfaceInfo("lo", "lo", InterfaceKind.Loopback, true, IPAddress.Loopback, 8);
            Assert.True(Validate("192.168.1.1", "255.255.255.0", "", "", "", down, loop).IsValid);

            // the chosen adapter's own current subnet (10.0.0.5/8 in the helper) is not an overlap
            Assert.True(Validate("10.1.1.1", "255.0.0.0").IsValid);
        }
    }

    public class AddressSuggesterTests
    {
        [Fact]
        public void FirstCandidate_WhenFree()
        {
            var s = AddressSuggester.Suggest("eth0", new[] { TestNet.Eth("eth0", ip: null) });
            Assert.Equal("192.168.137.1", s.Address.ToString());
            Assert.Equal(24, s.PrefixLength);
            Assert.Null(s.Gateway);
            Assert.Empty(s.DnsServers);
        }

        [Fact]
        public void SkipsOverlappingCandidates_InOrder()
        {
            var all = new[]
            {
                TestNet.Eth("eth0", ip: null),
                TestNet.Eth("eth1", "192.168.137.20"),
                TestNet.Eth("eth2", "192.168.138.5")
            };
            Assert.Equal("192.168.139.1", AddressSuggester.Suggest("eth0", all).Address.ToString());
        }

        [Fact]
        public void SkipsCandidateThatAnswers()
        {
            var prober = new FakeAddressProber();
            prober.InUse.Add("192.168.137.1");
            var s = AddressSuggester.Suggest("eth0", new[] { TestNet.Eth("eth0", ip: null) }, prober);
            Assert.Equal("192.168.138.1", s.Address.ToString());
        }

        [Fact]
        public void IgnoresChosenAdaptersOwnSubnet_AndDownInterfaces()
        {
            var all = new[] { TestNet.Eth("eth0", "192.168.137.9"), TestNet.Eth("eth1", "192.168.138.1", up: false) };
            Assert.Equal("192.168.137.1", AddressSuggester.Suggest("eth0", all).Address.ToString());
        }

        [Fact]
        public void LastResort_TenNet_ThenNull()
        {
            var all = AddressSuggester.Candidates().Take(AddressSuggester.Candidates().Count() - 1)
                .Select((c, i) => TestNet.Eth("x" + i, c.ToString().Substring(0, c.ToString().LastIndexOf('.')) + ".50"))
                .Concat(new[] { TestNet.Eth("eth0", ip: null) }).ToList();
            Assert.Equal("10.137.0.1", AddressSuggester.Suggest("eth0", all).Address.ToString());

            all.Add(TestNet.Eth("y", "10.137.0.9"));
            Assert.Null(AddressSuggester.Suggest("eth0", all));
        }

        [Fact]
        public void Candidates_AreDeterministic()
        {
            Assert.Equal(AddressSuggester.Candidates().Select(c => c.ToString()), AddressSuggester.Candidates().Select(c => c.ToString()));
        }
    }

    public class ChangePreviewTests
    {
        private static PreviewRow Row(ChangePreview p, string key)
        {
            return p.Rows.Single(r => r.LabelKey == key);
        }

        [Fact]
        public void RowOrder()
        {
            var p = ChangePreview.Create(AddressingTestData.Dhcp(), AddressingTestData.Settings());
            Assert.Equal(new[] { "Confirm_Mode", "Address_Address", "Address_Mask", "Address_Gateway", "Address_Dns" }, p.Rows.Select(r => r.LabelKey).ToArray());
        }

        [Fact]
        public void DhcpToStatic_AllRowsChange()
        {
            var p = ChangePreview.Create(AddressingTestData.Dhcp(), AddressingTestData.Settings("192.168.137.1", 24, null));
            Assert.Equal(PreviewKind.Automatic, Row(p, "Confirm_Mode").Current.Kind);
            Assert.Equal(PreviewKind.Static, Row(p, "Confirm_Mode").New.Kind);
            Assert.True(Row(p, "Confirm_Mode").Changed);
            Assert.Equal("10.20.30.40", Row(p, "Address_Address").Current.Text);
            Assert.Equal("192.168.137.1", Row(p, "Address_Address").New.Text);
            Assert.Equal("255.255.255.0", Row(p, "Address_Mask").New.Text);
            Assert.Equal(PreviewKind.None, Row(p, "Address_Gateway").New.Kind);
            Assert.Equal(PreviewKind.Automatic, Row(p, "Address_Dns").Current.Kind);
            Assert.Equal(PreviewKind.None, Row(p, "Address_Dns").New.Kind);
            Assert.True(Row(p, "Address_Dns").Changed);
        }

        [Fact]
        public void StaticToSameStatic_NothingChanged()
        {
            var current = AddressingTestData.Static(ip: "192.168.137.1", gateway: "192.168.137.254", dns: new[] { "8.8.8.8" });
            var p = ChangePreview.Create(current, AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254", "8.8.8.8"));
            Assert.All(p.Rows, r => Assert.False(r.Changed));
        }

        [Fact]
        public void GatewayAndDns_AddedAndRemoved()
        {
            var current = AddressingTestData.Static(gateway: "10.20.30.1", dns: new[] { "8.8.8.8", "1.1.1.1" });
            var p = ChangePreview.Create(current, AddressingTestData.Settings("10.20.30.40", 24, null));
            Assert.True(Row(p, "Address_Gateway").Changed);
            Assert.Equal("10.20.30.1", Row(p, "Address_Gateway").Current.Text);
            Assert.Equal(PreviewKind.None, Row(p, "Address_Gateway").New.Kind);
            Assert.Equal("8.8.8.8, 1.1.1.1", Row(p, "Address_Dns").Current.Text);
            Assert.Equal(PreviewKind.None, Row(p, "Address_Dns").New.Kind);
            Assert.False(Row(p, "Address_Address").Changed);
        }

        [Fact]
        public void SeveralCurrentAddresses_AreJoined()
        {
            var p = ChangePreview.Create(AddressingTestData.StaticMulti(), AddressingTestData.Settings());
            Assert.Equal("10.20.30.40, 172.16.5.5", Row(p, "Address_Address").Current.Text);
            Assert.Equal("255.255.255.0, 255.255.0.0", Row(p, "Address_Mask").Current.Text);
        }
    }

    public class RestoreFileTests
    {
        private static SavedConfiguration Sample(AdapterConfig previous, AdapterConfig applied = null, bool restoring = false)
        {
            return new SavedConfiguration("Ethernet 5", previous, applied ?? AdapterConfig.FromSettings(previous.AdapterId, previous.InterfaceIndex, AddressingTestData.Settings()),
                new DateTime(2026, 11, 5, 19, 30, 0, DateTimeKind.Utc), restoring);
        }

        [Fact]
        public void RoundTrip_StaticWithSeveralAddressesAndDns()
        {
            var parsed = RestoreFile.Parse(RestoreFile.Serialize(Sample(AddressingTestData.StaticMulti())));
            Assert.Equal("Ethernet 5", parsed.AdapterName);
            Assert.Equal("eth0", parsed.AdapterId);
            Assert.Equal(7, parsed.Previous.InterfaceIndex);
            Assert.True(AdapterConfig.Equivalent(AddressingTestData.StaticMulti(), parsed.Previous));
            Assert.Equal(2, parsed.Previous.Addresses.Count);
            Assert.Equal(new[] { "8.8.8.8", "1.1.1.1" }, parsed.Previous.DnsServers.Select(d => d.ToString()).ToArray());
            Assert.Equal(new DateTime(2026, 11, 5, 19, 30, 0, DateTimeKind.Utc), parsed.CreatedUtc);
            Assert.False(parsed.Restoring);
        }

        [Fact]
        public void RoundTrip_DhcpPrevious_HasNoAddressLines()
        {
            var text = RestoreFile.Serialize(Sample(AddressingTestData.Dhcp()));
            Assert.Contains("previous.mode=dhcp", text);
            Assert.DoesNotContain("previous.address=", text);
            var parsed = RestoreFile.Parse(text);
            Assert.Equal(AddressMode.Automatic, parsed.Previous.AddressMode);
            Assert.Equal(DnsMode.Automatic, parsed.Previous.DnsMode);
            Assert.Equal(AddressMode.Static, parsed.Applied.AddressMode);
        }

        [Fact]
        public void Format_MatchesContract()
        {
            var text = RestoreFile.Serialize(Sample(AddressingTestData.Static(dns: new[] { "8.8.8.8" })));
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("#DhcpServer restore v1", lines[0]);
            Assert.Contains("created=2026-11-05T19:30:00Z", lines);
            Assert.Contains("adapter=eth0", lines);
            Assert.Contains("index=7", lines);
            Assert.Contains("previous.address=10.20.30.40/24", lines);
            Assert.Contains("previous.gateway=10.20.30.1", lines);
            Assert.Contains("previous.dnsmode=static", lines);
            Assert.Contains("applied.gateway=none", lines);
        }

        [Fact]
        public void RestoringMarker_RoundTrips()
        {
            var parsed = RestoreFile.Parse(RestoreFile.Serialize(Sample(AddressingTestData.Dhcp(), restoring: true)));
            Assert.True(parsed.Restoring);
            Assert.Contains("restoring=1", RestoreFile.Serialize(Sample(AddressingTestData.Dhcp(), restoring: true)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("#other header\r\nadapter=x")]
        public void WrongHeader_IsCorrupt(string text)
        {
            Assert.Throws<RestoreRecordCorruptException>(() => RestoreFile.Parse(text));
        }

        [Theory]
        [InlineData("adapter=")]
        [InlineData("previous.mode=")]
        [InlineData("applied.mode=")]
        public void MissingKeys_AreCorrupt(string prefix)
        {
            var text = RestoreFile.Serialize(Sample(AddressingTestData.Static()));
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith(prefix, StringComparison.Ordinal));
            Assert.Throws<RestoreRecordCorruptException>(() => RestoreFile.Parse(string.Join("\r\n", lines)));
        }

        [Fact]
        public void UnknownKeysIgnored_AndLfAccepted()
        {
            var text = RestoreFile.Serialize(Sample(AddressingTestData.Static())).Replace("\r\n", "\n") + "future.key=1\n";
            Assert.NotNull(RestoreFile.Parse(text));
        }

        [Fact]
        public void BadAddressValue_IsCorrupt()
        {
            var text = RestoreFile.Serialize(Sample(AddressingTestData.Static())).Replace("previous.address=10.20.30.40/24", "previous.address=oops");
            Assert.Throws<RestoreRecordCorruptException>(() => RestoreFile.Parse(text));
        }
    }

    public class InterfaceConfigurableTests
    {
        [Theory]
        [InlineData(InterfaceKind.Ethernet, true)]
        [InlineData(InterfaceKind.Wireless, true)]
        [InlineData(InterfaceKind.Loopback, false)]
        [InlineData(InterfaceKind.Tunnel, false)]
        [InlineData(InterfaceKind.Other, false)]
        public void ByKind(InterfaceKind kind, bool expected)
        {
            Assert.Equal(expected, InterfaceEligibility.IsConfigurable(new InterfaceInfo("x", "x", kind, true, IPAddress.Parse("10.0.0.1"), 24)));
        }

        [Fact]
        public void AnyStatusAndAddressState_IsConfigurable_ButNotEligible()
        {
            var cases = new[]
            {
                TestNet.Eth(up: false),
                TestNet.Eth(ip: null),
                TestNet.Eth(ip: "169.254.1.1", prefix: 16),
                TestNet.Eth(prefix: 31)
            };
            foreach (var info in cases)
            {
                Assert.True(InterfaceEligibility.IsConfigurable(info));
                Assert.False(InterfaceEligibility.Evaluate(info, new[] { info }).IsEligible);
            }
        }
    }
}
