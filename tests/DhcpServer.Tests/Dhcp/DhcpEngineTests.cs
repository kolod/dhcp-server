using System;
using System.Linq;
using System.Net;
using DhcpServer.Core.Dhcp;
using DhcpServer.Core.Leases;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Dhcp
{
    public class DhcpEngineTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        private readonly AddressPool _pool = TestNet.Pool("192.168.137.1", 24, "192.168.137.254");
        private readonly LeaseTable _table = new LeaseTable();
        private readonly DhcpEngine _engine;

        public DhcpEngineTests()
        {
            _engine = new DhcpEngine(_pool, _table);
        }

        private DhcpMessage Send(byte[] datagram, DateTime? now = null)
        {
            var reply = _engine.Handle(datagram, now ?? T0);
            return reply == null ? null : TestPackets.Parse(reply);
        }

        private string Obtain(int mac, string requested = null, string host = null)
        {
            var offer = Send(TestPackets.Discover(mac, requested, host));
            var ack = Send(TestPackets.RequestSelecting(mac, offer.YiAddr.ToString(), host: host));
            Assert.Equal(DhcpMessageType.Ack, ack.MessageType);
            return ack.YiAddr.ToString();
        }

        [Fact]
        public void Discover_ProducesOfferWithExpectedOptions()
        {
            var offer = Send(TestPackets.Discover(1));

            Assert.Equal(DhcpMessageType.Offer, offer.MessageType);
            Assert.Equal(2, offer.Op);
            Assert.Equal(1u, offer.Xid);
            Assert.Equal(TestPackets.Mac(1), offer.ClientMac);
            Assert.Equal("192.168.137.2", offer.YiAddr.ToString());
            Assert.Equal("192.168.137.1", offer.GetAddressOption(DhcpOptionCode.ServerIdentifier).ToString());
            Assert.Equal("255.255.255.0", offer.GetAddressOption(DhcpOptionCode.SubnetMask).ToString());
            Assert.Equal("192.168.137.1", offer.GetAddressOption(DhcpOptionCode.Router).ToString());
            Assert.Equal("192.168.137.255", offer.GetAddressOption(DhcpOptionCode.BroadcastAddress).ToString());
            Assert.Equal(new byte[] { 0, 0, 0x38, 0x40 }, offer.Options[DhcpOptionCode.LeaseTime]);
            Assert.Equal(new byte[] { 0, 0, 0x1C, 0x20 }, offer.Options[DhcpOptionCode.RenewalTime]);
            Assert.Equal(new byte[] { 0, 0, 0x31, 0x38 }, offer.Options[DhcpOptionCode.RebindingTime]);
        }

        [Fact]
        public void FullExchange_CreatesActiveLease_WithHostName()
        {
            Assert.Equal("192.168.137.2", Obtain(1, host: "esp32"));
            var lease = _table.Get(TestPackets.MacKey(1));
            Assert.Equal("esp32", lease.HostName);
            Assert.Equal(LeaseState.Active, lease.GetState(T0));
        }

        [Fact]
        public void ReturningDevice_GetsSameAddress()
        {
            var first = Obtain(1);
            Obtain(2);
            var offer = Send(TestPackets.Discover(1), T0.AddHours(1));
            Assert.Equal(first, offer.YiAddr.ToString());

            var freshEngine = new DhcpEngine(_pool, _table);
            var again = TestPackets.Parse(freshEngine.Handle(TestPackets.Discover(1), T0.AddDays(1)));
            Assert.Equal(first, again.YiAddr.ToString());
        }

        [Fact]
        public void Discover_RequestedAddressHonored()
        {
            Assert.Equal("192.168.137.77", Obtain(1, "192.168.137.77"));
        }

        [Fact]
        public void Renew_UnicastRequestWithCiaddr_IsAcked()
        {
            var address = Obtain(1);
            var ack = Send(TestPackets.RequestRenew(1, address), T0.AddHours(2));
            Assert.Equal(DhcpMessageType.Ack, ack.MessageType);
            Assert.Equal(address, ack.YiAddr.ToString());
            Assert.Equal(T0.AddHours(6), _table.Get(TestPackets.MacKey(1)).Expires);
            Assert.Single(_table.Snapshot());
        }

        [Fact]
        public void InitReboot_FreeAddress_IsAcked()
        {
            var ack = Send(TestPackets.RequestInitReboot(1, "192.168.137.50"));
            Assert.Equal(DhcpMessageType.Ack, ack.MessageType);
        }

        [Fact]
        public void InitReboot_AddressOfAnotherDevice_IsNaked()
        {
            var taken = Obtain(1);
            var nak = Send(TestPackets.RequestInitReboot(2, taken));
            Assert.Equal(DhcpMessageType.Nak, nak.MessageType);
            Assert.Equal("0.0.0.0", nak.YiAddr.ToString());
            Assert.Equal(new[] { DhcpOptionCode.MessageType, DhcpOptionCode.ServerIdentifier }, nak.Options.Keys.ToArray());
        }

        [Fact]
        public void InitReboot_OutsideSubnet_IsNaked()
        {
            Assert.Equal(DhcpMessageType.Nak, Send(TestPackets.RequestInitReboot(1, "10.0.0.5")).MessageType);
        }

        [Theory]
        [InlineData("192.168.137.1")]
        [InlineData("192.168.137.254")]
        [InlineData("192.168.137.0")]
        [InlineData("192.168.137.255")]
        public void Request_ForServerGatewayNetworkOrBroadcast_IsNaked(string address)
        {
            Assert.Equal(DhcpMessageType.Nak, Send(TestPackets.RequestInitReboot(1, address)).MessageType);
            Assert.Equal(DhcpMessageType.Nak, Send(TestPackets.RequestSelecting(1, address)).MessageType);
        }

        [Fact]
        public void Request_ForAnotherServer_IsIgnored_AndDropsOffer()
        {
            var offer = Send(TestPackets.Discover(1));
            Assert.Null(Send(TestPackets.RequestSelecting(1, offer.YiAddr.ToString(), serverId: "192.168.137.200")));
            Assert.Equal(offer.YiAddr, Send(TestPackets.Discover(2)).YiAddr); // offer was released
        }

        [Fact]
        public void Request_WithoutAnyAddress_IsIgnored()
        {
            Assert.Null(Send(TestPackets.Base(1, DhcpMessageType.Request).ToBytes()));
        }

        [Fact]
        public void Release_MarksLeaseReleased_NoReply()
        {
            var address = Obtain(1);
            Assert.Null(Send(TestPackets.Release(1, address)));
            Assert.Equal(LeaseState.Released, _table.Get(TestPackets.MacKey(1)).GetState(T0));
        }

        [Fact]
        public void Release_ByOtherDevice_IsIgnored()
        {
            var address = Obtain(1);
            Send(TestPackets.Release(2, address));
            Assert.Equal(LeaseState.Active, _table.Get(TestPackets.MacKey(1)).GetState(T0));
        }

        [Fact]
        public void Decline_RemovesLease_AndAddressIsAvoided()
        {
            var address = Obtain(1);
            Assert.Null(Send(TestPackets.Decline(1, address)));
            Assert.Empty(_table.Snapshot());
            Assert.NotEqual(address, Send(TestPackets.Discover(2)).YiAddr.ToString());
        }

        [Fact]
        public void Inform_GetsAckWithoutLease()
        {
            var ack = Send(TestPackets.Inform(1, "192.168.137.60"));
            Assert.Equal(DhcpMessageType.Ack, ack.MessageType);
            Assert.Equal("0.0.0.0", ack.YiAddr.ToString());
            Assert.False(ack.HasOption(DhcpOptionCode.LeaseTime));
            Assert.Equal("255.255.255.0", ack.GetAddressOption(DhcpOptionCode.SubnetMask).ToString());
            Assert.Empty(_table.Snapshot());
        }

        [Fact]
        public void Relayed_AndNonRequest_AreIgnored()
        {
            var relayed = TestPackets.Base(1, DhcpMessageType.Discover);
            relayed.GiAddr = IPAddress.Parse("10.0.0.1");
            Assert.Null(Send(relayed.ToBytes()));

            var reply = TestPackets.Base(1, DhcpMessageType.Discover);
            reply.Op = 2;
            Assert.Null(Send(reply.ToBytes()));

            var offer = TestPackets.Base(1, DhcpMessageType.Offer);
            Assert.Null(Send(offer.ToBytes()));
        }

        [Fact]
        public void Malformed_IsIgnored()
        {
            Assert.Null(_engine.Handle(new byte[10], T0));
            Assert.Null(_engine.Handle(null, T0));
            var random = new Random(7);
            for (var i = 0; i < 500; i++)
            {
                var data = new byte[random.Next(0, 400)];
                random.NextBytes(data);
                _engine.Handle(data, T0);
            }
        }

        [Fact]
        public void PoolExhausted_RaisesEvent_AndKeepsLeases()
        {
            var raised = 0;
            _engine.PoolExhausted += (s, e) => raised++;
            for (var i = 1; i <= _pool.Addresses.Count; i++)
            {
                Obtain(i);
            }

            Assert.Null(Send(TestPackets.Discover(999)));
            Assert.Equal(1, raised);
            Assert.Equal(_pool.Addresses.Count, _table.Snapshot().Count);
        }

        [Fact]
        public void YiAddr_NeverOutsidePoolOrReserved()
        {
            for (var i = 1; i <= 300; i++)
            {
                var offer = Send(TestPackets.Discover(i));
                if (offer == null)
                {
                    break;
                }

                Assert.True(_pool.Contains(offer.YiAddr));
                Send(TestPackets.RequestSelecting(i, offer.YiAddr.ToString()));
            }
        }

        [Fact]
        public void UsedAddress_IsNotOffered()
        {
            var prober = new FakeAddressProber();
            prober.InUse.Add("192.168.137.2");
            var engine = new DhcpEngine(_pool, new LeaseTable(), a => prober.IsInUse(a, null));
            var offer = TestPackets.Parse(engine.Handle(TestPackets.Discover(1), T0));
            Assert.Equal("192.168.137.3", offer.YiAddr.ToString());
        }
    }
}
