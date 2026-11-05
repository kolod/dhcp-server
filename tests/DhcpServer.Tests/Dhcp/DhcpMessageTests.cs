using System;
using System.Net;
using DhcpServer.Core.Dhcp;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Dhcp
{
    public class DhcpMessageTests
    {
        [Fact]
        public void RoundTrip_PreservesHeaderAndOptions()
        {
            var m = TestPackets.Base(7, DhcpMessageType.Request, 0xDEADBEEF);
            m.Flags = 0x8000;
            m.CiAddr = IPAddress.Parse("10.0.0.9");
            m.SetAddressOption(DhcpOptionCode.RequestedAddress, IPAddress.Parse("10.0.0.20"));
            m.SetAddressOption(DhcpOptionCode.ServerIdentifier, IPAddress.Parse("10.0.0.1"));
            m.SetUInt32Option(DhcpOptionCode.LeaseTime, 14400);
            m.Options[DhcpOptionCode.HostName] = System.Text.Encoding.ASCII.GetBytes("esp32");

            var parsed = TestPackets.Parse(m.ToBytes());

            Assert.Equal(1, parsed.Op);
            Assert.Equal(0xDEADBEEF, parsed.Xid);
            Assert.Equal(0x8000, parsed.Flags);
            Assert.Equal(m.ClientMac, parsed.ClientMac);
            Assert.Equal(IPAddress.Parse("10.0.0.9"), parsed.CiAddr);
            Assert.Equal(DhcpMessageType.Request, parsed.MessageType);
            Assert.Equal(IPAddress.Parse("10.0.0.20"), parsed.GetAddressOption(DhcpOptionCode.RequestedAddress));
            Assert.Equal(IPAddress.Parse("10.0.0.1"), parsed.GetAddressOption(DhcpOptionCode.ServerIdentifier));
            Assert.Equal(new byte[] { 0, 0, 0x38, 0x40 }, parsed.Options[DhcpOptionCode.LeaseTime]);
            Assert.Equal("esp32", System.Text.Encoding.ASCII.GetString(parsed.Options[DhcpOptionCode.HostName]));
        }

        [Fact]
        public void ToBytes_IsAtLeastBootpMinimum()
        {
            Assert.True(TestPackets.Base(1, DhcpMessageType.Discover).ToBytes().Length >= 300);
        }

        [Fact]
        public void TooShort_IsRejected()
        {
            DhcpMessage m;
            Assert.False(DhcpMessage.TryParse(new byte[239], out m));
            Assert.False(DhcpMessage.TryParse(null, out m));
        }

        [Fact]
        public void BadMagicCookie_IsRejected()
        {
            var data = TestPackets.Discover(1);
            data[236] = 0;
            DhcpMessage m;
            Assert.False(DhcpMessage.TryParse(data, out m));
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 8)]
        public void NonEthernetHardware_IsRejected(int offset, int value)
        {
            var data = TestPackets.Discover(1);
            data[offset] = (byte)value;
            DhcpMessage m;
            Assert.False(DhcpMessage.TryParse(data, out m));
        }

        [Fact]
        public void MissingMessageType_IsRejected()
        {
            var m = new DhcpMessage { Op = 1, ClientMac = TestPackets.Mac(1) };
            m.SetUInt32Option(DhcpOptionCode.LeaseTime, 1);
            DhcpMessage parsed;
            Assert.False(DhcpMessage.TryParse(m.ToBytes(), out parsed));
        }

        [Fact]
        public void TruncatedOption_IsRejected()
        {
            var data = new byte[242];
            var good = TestPackets.Discover(1);
            Array.Copy(good, data, 240);
            data[240] = DhcpOptionCode.MessageType;
            data[241] = 5; // claims 5 bytes, none present
            DhcpMessage m;
            Assert.False(DhcpMessage.TryParse(data, out m));
        }

        [Fact]
        public void RandomGarbage_NeverThrows()
        {
            var random = new Random(42);
            for (var i = 0; i < 2000; i++)
            {
                var data = new byte[random.Next(0, 400)];
                random.NextBytes(data);
                if (data.Length >= 240 && i % 2 == 0)
                {
                    data[236] = 99; data[237] = 130; data[238] = 83; data[239] = 99;
                    data[1] = 1; data[2] = 6;
                }

                DhcpMessage m;
                DhcpMessage.TryParse(data, out m);
            }
        }

        [Fact]
        public void PadOptions_AreSkipped()
        {
            var good = TestPackets.Discover(1);
            var data = new byte[good.Length + 3];
            Array.Copy(good, data, 240);
            data[240] = 0; data[241] = 0;
            Array.Copy(good, 240, data, 242, good.Length - 240);
            DhcpMessage m;
            Assert.True(DhcpMessage.TryParse(data, out m));
            Assert.Equal(DhcpMessageType.Discover, m.MessageType);
        }
    }
}
