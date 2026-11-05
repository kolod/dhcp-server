using System.Collections.Generic;
using System.Net;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Network
{
    public class IpMathTests
    {
        [Theory]
        [InlineData("192.168.137.1", 24, "192.168.137.0", "192.168.137.255", "255.255.255.0")]
        [InlineData("10.1.2.3", 30, "10.1.2.0", "10.1.2.3", "255.255.255.252")]
        [InlineData("172.16.5.9", 16, "172.16.0.0", "172.16.255.255", "255.255.0.0")]
        public void Subnet_Boundaries(string ip, int prefix, string network, string broadcast, string mask)
        {
            var address = IPAddress.Parse(ip);
            Assert.Equal(network, IpMath.FromUInt32(IpMath.Network(address, prefix)).ToString());
            Assert.Equal(broadcast, IpMath.FromUInt32(IpMath.Broadcast(address, prefix)).ToString());
            Assert.Equal(mask, IpMath.MaskAddress(prefix).ToString());
        }

        [Fact]
        public void Mask_ZeroAndFull()
        {
            Assert.Equal(0u, IpMath.Mask(0));
            Assert.Equal(uint.MaxValue, IpMath.Mask(32));
        }

        [Theory]
        [InlineData("192.168.1.5", 24, "192.168.1.200", 24, true)]
        [InlineData("192.168.1.5", 24, "192.168.2.5", 24, false)]
        [InlineData("10.0.0.5", 8, "10.9.9.9", 24, true)]
        [InlineData("10.0.0.5", 24, "10.9.9.9", 8, true)]
        public void Overlaps(string a, int pa, string b, int pb, bool expected)
        {
            Assert.Equal(expected, IpMath.Overlaps(IPAddress.Parse(a), pa, IPAddress.Parse(b), pb));
        }

        [Fact]
        public void RoundTrip_UInt32()
        {
            var ip = IPAddress.Parse("1.2.3.4");
            Assert.Equal(ip, IpMath.FromUInt32(IpMath.ToUInt32(ip)));
        }

        [Fact]
        public void LinkLocalAndLoopback()
        {
            Assert.True(IpMath.IsLinkLocal(IPAddress.Parse("169.254.3.4")));
            Assert.False(IpMath.IsLinkLocal(IPAddress.Parse("169.253.3.4")));
            Assert.True(IpMath.IsLoopback(IPAddress.Parse("127.0.0.1")));
        }
    }

    public class InterfaceEligibilityTests
    {
        private static Eligibility Evaluate(InterfaceInfo candidate, params InterfaceInfo[] others)
        {
            var all = new List<InterfaceInfo>(others) { candidate };
            return InterfaceEligibility.Evaluate(candidate, all);
        }

        [Fact]
        public void EthernetUp_IsEligible()
        {
            Assert.True(Evaluate(TestNet.Eth()).IsEligible);
        }

        [Fact]
        public void Wireless_IsEligible()
        {
            var wifi = new InterfaceInfo("w", "Wi-Fi", InterfaceKind.Wireless, true, IPAddress.Parse("192.168.50.1"), 24);
            Assert.True(Evaluate(wifi).IsEligible);
        }

        [Fact]
        public void Down_IsRefused()
        {
            Assert.Equal(FailureKind.Down, Evaluate(TestNet.Eth(up: false)).Reason.Kind);
        }

        [Theory]
        [InlineData(InterfaceKind.Loopback)]
        [InlineData(InterfaceKind.Tunnel)]
        [InlineData(InterfaceKind.Other)]
        public void UnsupportedType_IsRefused(InterfaceKind kind)
        {
            var info = new InterfaceInfo("x", "x", kind, true, IPAddress.Parse("192.168.5.1"), 24);
            Assert.Equal(FailureKind.NotSupportedType, Evaluate(info).Reason.Kind);
        }

        [Fact]
        public void NoAddress_IsRefused()
        {
            Assert.Equal(FailureKind.NoIpv4Address, Evaluate(TestNet.Eth(ip: null)).Reason.Kind);
        }

        [Fact]
        public void LinkLocal_IsRefused()
        {
            Assert.Equal(FailureKind.LinkLocalAddress, Evaluate(TestNet.Eth(ip: "169.254.10.10", prefix: 16)).Reason.Kind);
        }

        [Theory]
        [InlineData(31)]
        [InlineData(32)]
        public void TinySubnet_IsRefused(int prefix)
        {
            Assert.Equal(FailureKind.SubnetTooSmall, Evaluate(TestNet.Eth(prefix: prefix)).Reason.Kind);
        }

        [Fact]
        public void OverlapWithOtherActiveInterface_IsRefused()
        {
            var other = TestNet.Eth("eth1", "192.168.137.77");
            Assert.Equal(FailureKind.SubnetOverlap, Evaluate(TestNet.Eth(), other).Reason.Kind);
        }

        [Fact]
        public void OverlapWithDownInterface_IsIgnored()
        {
            var other = TestNet.Eth("eth1", "192.168.137.77", up: false);
            Assert.True(Evaluate(TestNet.Eth(), other).IsEligible);
        }

        [Fact]
        public void OtherSubnet_IsNotAnOverlap()
        {
            var other = TestNet.Eth("eth1", "10.0.0.5");
            Assert.True(Evaluate(TestNet.Eth(), other).IsEligible);
        }

        [Fact]
        public void RuleOrder_DownBeforeType()
        {
            var info = new InterfaceInfo("x", "x", InterfaceKind.Tunnel, false, null, 24);
            Assert.Equal(FailureKind.Down, Evaluate(info).Reason.Kind);
        }
    }
}
