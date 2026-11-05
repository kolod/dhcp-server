using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DhcpServer.Core.Leases;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Leases
{
    public class AddressPoolTests
    {
        [Fact]
        public void Slash24_ExcludesNetworkBroadcastAndServer()
        {
            var pool = TestNet.Pool();
            Assert.False(pool.Contains(IPAddress.Parse("192.168.137.0")));
            Assert.False(pool.Contains(IPAddress.Parse("192.168.137.255")));
            Assert.False(pool.Contains(IPAddress.Parse("192.168.137.1")));
            Assert.True(pool.Contains(IPAddress.Parse("192.168.137.2")));
            Assert.True(pool.Contains(IPAddress.Parse("192.168.137.254")));
            Assert.Equal(253, pool.Addresses.Count);
        }

        [Fact]
        public void GatewayInSubnet_IsExcluded()
        {
            var pool = TestNet.Pool("192.168.137.1", 24, "192.168.137.254");
            Assert.False(pool.Contains(IPAddress.Parse("192.168.137.254")));
            Assert.Equal(252, pool.Addresses.Count);
        }

        [Fact]
        public void GatewayOutsideSubnet_IsIgnored()
        {
            var pool = TestNet.Pool("192.168.137.1", 24, "10.0.0.1");
            Assert.Equal(253, pool.Addresses.Count);
        }

        [Fact]
        public void Slash30_HasOneUsableAddress()
        {
            var pool = TestNet.Pool("10.0.0.1", 30);
            Assert.Single(pool.Addresses);
            Assert.Equal("10.0.0.2", pool.Addresses[0].ToString());
        }

        [Fact]
        public void WideSubnet_IsCappedToWindow()
        {
            var pool = TestNet.Pool("10.1.0.1", 16);
            Assert.Equal(AddressPool.WindowSize, pool.Addresses.Count);
            Assert.Equal("10.1.0.2", pool.Addresses[0].ToString());
            Assert.False(pool.Contains(IPAddress.Parse("10.1.9.9")));
        }

        [Fact]
        public void Order_IsAscending()
        {
            var pool = TestNet.Pool();
            Assert.Equal(pool.Addresses.OrderBy(a => a.GetAddressBytes()[3]).ToList(), pool.Addresses.ToList());
        }

        [Fact]
        public void Exposes_MaskAndBroadcast()
        {
            var pool = TestNet.Pool();
            Assert.Equal("255.255.255.0", pool.Mask.ToString());
            Assert.Equal("192.168.137.255", pool.Broadcast.ToString());
        }
    }

    public class LeaseTableTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        private readonly AddressPool _pool = TestNet.Pool();
        private readonly LeaseTable _table = new LeaseTable();

        private IPAddress Lease(int mac, DateTime now, string requested = null)
        {
            var offer = _table.Offer(TestPackets.MacKey(mac), requested == null ? null : IPAddress.Parse(requested), _pool, now, null);
            Assert.NotNull(offer);
            Assert.NotNull(_table.Confirm(TestPackets.MacKey(mac), offer, null, _pool, now));
            return offer;
        }

        [Fact]
        public void FirstDevice_GetsLowestAddress()
        {
            Assert.Equal("192.168.137.2", Lease(1, T0).ToString());
            Assert.Equal("192.168.137.3", Lease(2, T0).ToString());
        }

        [Fact]
        public void SameMac_GetsSameAddress()
        {
            var first = Lease(1, T0);
            Lease(2, T0);
            Assert.Equal(first, Lease(1, T0.AddHours(1)));
        }

        [Fact]
        public void SameMac_GetsSameAddress_AfterExpiryAndRelease()
        {
            var first = Lease(1, T0);
            var later = T0.AddDays(2);
            Assert.Equal(first, _table.Offer(TestPackets.MacKey(1), null, _pool, later, null));
            _table.Release(TestPackets.MacKey(1), first, T0);
            Assert.Equal(first, _table.Offer(TestPackets.MacKey(1), null, _pool, T0, null));
        }

        [Fact]
        public void RequestedAddress_IsHonoredWhenFree()
        {
            Assert.Equal("192.168.137.50", Lease(1, T0, "192.168.137.50").ToString());
        }

        [Fact]
        public void RequestedAddress_TakenByOther_IsSkipped()
        {
            Lease(1, T0, "192.168.137.50");
            Assert.NotEqual("192.168.137.50", Lease(2, T0, "192.168.137.50").ToString());
        }

        [Fact]
        public void RequestedAddress_OutsidePool_IsIgnored()
        {
            Assert.Equal("192.168.137.2", Lease(1, T0, "192.168.137.1").ToString());
            Assert.Equal("192.168.137.3", Lease(2, T0, "10.0.0.5").ToString());
        }

        [Fact]
        public void Offer_ReservesAddressFor60Seconds()
        {
            var a = _table.Offer(TestPackets.MacKey(1), null, _pool, T0, null);
            var b = _table.Offer(TestPackets.MacKey(2), null, _pool, T0.AddSeconds(30), null);
            Assert.NotEqual(a, b);
            var c = _table.Offer(TestPackets.MacKey(3), null, _pool, T0.AddSeconds(61), null);
            Assert.Equal(a, c);
        }

        [Fact]
        public void NoTwoMacs_ShareAnAddress()
        {
            var seen = new HashSet<string>();
            for (var i = 1; i <= 40; i++)
            {
                Assert.True(seen.Add(Lease(i, T0).ToString()));
            }
        }

        [Fact]
        public void Confirm_RejectsAddressHeldByAnother()
        {
            var a = Lease(1, T0);
            Assert.Null(_table.Confirm(TestPackets.MacKey(2), a, null, _pool, T0));
        }

        [Fact]
        public void Confirm_RejectsAddressOutsidePool()
        {
            Assert.Null(_table.Confirm(TestPackets.MacKey(1), IPAddress.Parse("192.168.137.1"), null, _pool, T0));
            Assert.Null(_table.Confirm(TestPackets.MacKey(1), IPAddress.Parse("10.0.0.2"), null, _pool, T0));
        }

        [Fact]
        public void Lease_ExpiresAfterFourHours()
        {
            Lease(1, T0);
            var lease = _table.Get(TestPackets.MacKey(1));
            Assert.Equal(LeaseState.Active, lease.GetState(T0.AddHours(3)));
            Assert.Equal(LeaseState.Expired, lease.GetState(T0.AddHours(4)));
        }

        [Fact]
        public void Release_MarksReleased_AndRaisesChanged()
        {
            var a = Lease(1, T0);
            var changes = 0;
            _table.Changed += (s, e) => changes++;
            _table.Release(TestPackets.MacKey(1), a, T0);
            Assert.Equal(LeaseState.Released, _table.Get(TestPackets.MacKey(1)).GetState(T0));
            Assert.Equal(1, changes);
        }

        [Fact]
        public void Renewal_UpdatesExpiry_WithoutDuplicate()
        {
            var a = Lease(1, T0);
            Assert.NotNull(_table.Confirm(TestPackets.MacKey(1), a, null, _pool, T0.AddHours(2)));
            Assert.Single(_table.Snapshot());
            Assert.Equal(T0.AddHours(6), _table.Get(TestPackets.MacKey(1)).Expires);
        }

        [Fact]
        public void HostName_IsKept_WhenRenewalOmitsIt()
        {
            var offer = _table.Offer(TestPackets.MacKey(1), null, _pool, T0, null);
            _table.Confirm(TestPackets.MacKey(1), offer, "esp32", _pool, T0);
            _table.Confirm(TestPackets.MacKey(1), offer, null, _pool, T0.AddHours(1));
            Assert.Equal("esp32", _table.Get(TestPackets.MacKey(1)).HostName);
        }

        [Fact]
        public void Reuse_PrefersNeverUsed_ThenOldest()
        {
            // Fill the pool, release two, then check that a newcomer gets the least recently expired one.
            var addresses = new Dictionary<int, IPAddress>();
            for (var i = 1; i <= 253; i++)
            {
                addresses[i] = Lease(i, T0.AddSeconds(i));
            }

            Assert.Null(_table.Offer(TestPackets.MacKey(300), null, _pool, T0.AddMinutes(10), null));

            var later = T0.AddDays(2); // everything expired
            var reused = _table.Offer(TestPackets.MacKey(300), null, _pool, later, null);
            Assert.Equal(addresses[1], reused);
        }

        [Fact]
        public void Exhaustion_ReturnsNull_AndKeepsExistingLeases()
        {
            for (var i = 1; i <= 253; i++)
            {
                Lease(i, T0);
            }

            Assert.Null(_table.Offer(TestPackets.MacKey(254), null, _pool, T0.AddMinutes(1), null));
            Assert.Equal(253, _table.Snapshot().Count);
        }

        [Fact]
        public void Probe_SkipsAddressesInUse_AndRemembersThem()
        {
            var prober = new FakeAddressProber();
            prober.InUse.Add("192.168.137.2");
            var offer = _table.Offer(TestPackets.MacKey(1), null, _pool, T0, a => prober.IsInUse(a, null));
            Assert.Equal("192.168.137.3", offer.ToString());

            prober.Asked.Clear();
            prober.InUse.Clear();
            var again = _table.Offer(TestPackets.MacKey(2), null, _pool, T0.AddMinutes(5), a => prober.IsInUse(a, null));
            Assert.NotEqual("192.168.137.2", again.ToString()); // still remembered
            var much = _table.Offer(TestPackets.MacKey(3), null, _pool, T0.AddMinutes(11), a => prober.IsInUse(a, null));
            Assert.Equal("192.168.137.2", much.ToString()); // memory expired
        }

        [Fact]
        public void Probe_IsBypassed_ForDeviceWithValidLease()
        {
            var a = Lease(1, T0);
            var asked = new List<string>();
            var offer = _table.Offer(TestPackets.MacKey(1), null, _pool, T0.AddHours(1), x => { asked.Add(x.ToString()); return true; });
            Assert.Equal(a, offer);
            Assert.Empty(asked);
        }

        [Fact]
        public void Decline_DropsLease_AndAvoidsAddress()
        {
            var a = Lease(1, T0);
            _table.Decline(TestPackets.MacKey(1), a, T0);
            Assert.Empty(_table.Snapshot());
            Assert.NotEqual(a, _table.Offer(TestPackets.MacKey(2), null, _pool, T0.AddMinutes(1), null));
        }

        [Fact]
        public void Snapshot_IsOrderedNumericallyByAddress()
        {
            Lease(1, T0, "192.168.137.100");
            Lease(2, T0, "192.168.137.20");
            Lease(3, T0, "192.168.137.3");
            Assert.Equal(new[] { "192.168.137.3", "192.168.137.20", "192.168.137.100" },
                _table.Snapshot().Select(l => l.Address.ToString()).ToArray());
        }

        [Fact]
        public void Changed_FiresOncePerMutation_NotForOffers()
        {
            var changes = 0;
            _table.Changed += (s, e) => changes++;
            var o = _table.Offer(TestPackets.MacKey(1), null, _pool, T0, null);
            Assert.Equal(0, changes);
            _table.Confirm(TestPackets.MacKey(1), o, null, _pool, T0);
            Assert.Equal(1, changes);
        }

        [Fact]
        public void LeasesOfOtherSubnet_DoNotBlockAllocation()
        {
            var old = new Lease(TestPackets.MacKey(9), IPAddress.Parse("192.168.137.2"), "", T0, T0.AddHours(100), false, 16);
            _table.Load(new[] { old });
            Assert.Equal("192.168.137.2", Lease(1, T0).ToString());
        }

        [Fact]
        public void Load_DeduplicatesAddresses()
        {
            var a = new Lease(TestPackets.MacKey(1), IPAddress.Parse("192.168.137.2"), "", T0, T0.AddHours(1), false, 24);
            var b = new Lease(TestPackets.MacKey(2), IPAddress.Parse("192.168.137.2"), "", T0, T0.AddHours(2), false, 24);
            _table.Load(new[] { a, b });
            Assert.Single(_table.Snapshot());
            Assert.Equal(TestPackets.MacKey(2), _table.Snapshot()[0].Mac);
        }
    }

    public class LeaseFileTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc);

        private static Lease Sample(int mac, string ip, string host = "esp32", bool released = false)
        {
            return new Lease(TestPackets.MacKey(mac), IPAddress.Parse(ip), host, T0, T0.AddHours(4), released, 24);
        }

        [Fact]
        public void RoundTrip()
        {
            var text = LeaseFile.Serialize(new[] { Sample(1, "192.168.137.12"), Sample(2, "192.168.137.3", "", true) }, T0);
            var parsed = LeaseFile.Parse(text);
            Assert.Equal(2, parsed.Count);
            var one = parsed.Single(l => l.Mac == TestPackets.MacKey(1));
            Assert.Equal("192.168.137.12", one.Address.ToString());
            Assert.Equal("esp32", one.HostName);
            Assert.Equal(T0, one.Start);
            Assert.Equal(T0.AddHours(4), one.Expires);
            Assert.False(one.IsReleased);
            Assert.True(parsed.Single(l => l.Mac == TestPackets.MacKey(2)).IsReleased);
        }

        [Fact]
        public void Serialize_FormatMatchesContract()
        {
            var text = LeaseFile.Serialize(new[] { Sample(1, "192.168.137.12") }, T0);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal("#DhcpServer leases v1", lines[0]);
            Assert.Equal("aabbcc000001\t192.168.137.12\t24\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tA\tesp32", lines[1]);
        }

        [Fact]
        public void WrongHeader_IgnoresWholeFile()
        {
            Assert.Empty(LeaseFile.Parse("#other\r\naabbcc000001\t192.168.137.12\t24\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tA\t"));
            Assert.Empty(LeaseFile.Parse(string.Empty));
        }

        [Fact]
        public void CorruptLines_AreSkipped_OthersLoad()
        {
            var text = "#DhcpServer leases v1\n"
                + "garbage\n"
                + "zz\t192.168.137.12\t24\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tA\t\n"
                + "aabbcc000001\t999.1.1.1\t24\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tA\t\n"
                + "aabbcc000002\t192.168.137.13\t99\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tA\t\n"
                + "aabbcc000003\t192.168.137.14\t24\tnot-a-date\t2026-11-05T22:30:00Z\tA\t\n"
                + "aabbcc000004\t192.168.137.15\t24\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tX\t\n"
                + "# comment\n\n"
                + "aabbcc000005\t192.168.137.16\t24\t2026-11-05T18:30:00Z\t2026-11-05T22:30:00Z\tA\tgood\n";
            var parsed = LeaseFile.Parse(text);
            Assert.Single(parsed);
            Assert.Equal("good", parsed[0].HostName);
        }

        [Fact]
        public void Duplicates_LaterExpiryWins()
        {
            var a = new Lease(TestPackets.MacKey(1), IPAddress.Parse("192.168.137.2"), "", T0, T0.AddHours(1), false, 24);
            var b = new Lease(TestPackets.MacKey(1), IPAddress.Parse("192.168.137.3"), "", T0, T0.AddHours(2), false, 24);
            var c = new Lease(TestPackets.MacKey(2), IPAddress.Parse("192.168.137.3"), "", T0, T0.AddHours(3), false, 24);
            var text = LeaseFile.Header + "\r\n" + string.Join("\r\n", new[] { a, b, c }.Select(Line));
            var parsed = LeaseFile.Parse(text);
            Assert.Single(parsed);
            Assert.Equal(TestPackets.MacKey(2), parsed[0].Mac);
        }

        private static string Line(Lease l)
        {
            return LeaseFile.Serialize(new[] { l }, T0).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1];
        }

        [Fact]
        public void HostName_IsSanitizedAndCapped()
        {
            var lease = new Lease(TestPackets.MacKey(1), IPAddress.Parse("192.168.137.2"), "a\tb\nc" + new string('x', 100) + "é", T0, T0.AddHours(1), false, 24);
            Assert.DoesNotContain("\t", lease.HostName);
            Assert.DoesNotContain("\n", lease.HostName);
            Assert.Equal(63, lease.HostName.Length);
            Assert.DoesNotContain("é", lease.HostName);
        }

        [Fact]
        public void MoreThan1024_DropsOldestInactiveFirst()
        {
            var leases = new List<Lease>();
            for (var i = 0; i < 1030; i++)
            {
                var ip = IPAddress.Parse("10." + (i / 250) + "." + (i % 250) + ".1");
                var active = i >= 1020; // last 10 are active
                var expires = active ? T0.AddHours(10) : T0.AddMinutes(-1030 + i);
                leases.Add(new Lease(MacAddress.ToKey(new byte[] { 1, 2, 3, (byte)(i >> 8), (byte)i, 0 }), ip, "", T0, expires, false, 24));
            }

            var parsed = LeaseFile.Parse(LeaseFile.Serialize(leases, T0));
            Assert.Equal(LeaseFile.MaxEntries, parsed.Count);
            Assert.Equal(10, parsed.Count(l => l.IsActive(T0)));
        }

        [Fact]
        public void AcceptsLfAndCrLf()
        {
            var text = LeaseFile.Serialize(new[] { Sample(1, "192.168.137.12") }, T0).Replace("\r\n", "\n");
            Assert.Single(LeaseFile.Parse(text));
        }

        [Fact]
        public void MacFormat_IsColonSeparatedUpper()
        {
            Assert.Equal("AA:BB:CC:00:00:01", MacAddress.Format(TestPackets.MacKey(1)));
        }
    }
}
