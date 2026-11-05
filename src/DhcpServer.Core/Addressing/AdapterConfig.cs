using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace DhcpServer.Core.Addressing
{
    public enum AddressMode
    {
        Automatic,
        Static
    }

    public enum DnsMode
    {
        Automatic,
        Static
    }

    public sealed class AdapterAddress
    {
        public AdapterAddress(IPAddress address, int prefixLength)
        {
            Address = address;
            PrefixLength = prefixLength;
        }

        public IPAddress Address { get; }

        public int PrefixLength { get; }

        public bool SameAs(AdapterAddress other)
        {
            return other != null && Address.Equals(other.Address) && PrefixLength == other.PrefixLength;
        }
    }

    /// <summary>A snapshot of one adapter's IPv4 configuration. Immutable.</summary>
    public sealed class AdapterConfig
    {
        public AdapterConfig(
            string adapterId,
            int interfaceIndex,
            IEnumerable<AdapterAddress> addresses,
            IPAddress gateway,
            IEnumerable<IPAddress> dnsServers,
            AddressMode addressMode,
            DnsMode dnsMode)
        {
            AdapterId = adapterId;
            InterfaceIndex = interfaceIndex;
            Addresses = (addresses ?? Enumerable.Empty<AdapterAddress>()).ToList();
            Gateway = gateway;
            DnsServers = (dnsServers ?? Enumerable.Empty<IPAddress>()).ToList();
            AddressMode = addressMode;
            DnsMode = dnsMode;
        }

        public string AdapterId { get; }

        public int InterfaceIndex { get; }

        public IReadOnlyList<AdapterAddress> Addresses { get; }

        public IPAddress Gateway { get; }

        /// <summary>Static DNS servers (empty when the DNS mode is automatic).</summary>
        public IReadOnlyList<IPAddress> DnsServers { get; }

        public AddressMode AddressMode { get; }

        public DnsMode DnsMode { get; }

        /// <summary>The configuration the program sets for a session.</summary>
        public static AdapterConfig FromSettings(string adapterId, int interfaceIndex, AddressSettings settings)
        {
            return new AdapterConfig(
                adapterId,
                interfaceIndex,
                new[] { new AdapterAddress(settings.Address, settings.PrefixLength) },
                settings.Gateway,
                settings.DnsServers,
                AddressMode.Static,
                DnsMode.Static);
        }

        /// <summary>
        /// Same effective configuration? For an automatic address mode only the mode counts (the leased
        /// address is not ours to compare); likewise for an automatic DNS mode.
        /// </summary>
        public static bool Equivalent(AdapterConfig a, AdapterConfig b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.AddressMode != b.AddressMode)
            {
                return false;
            }

            // "No static DNS servers" looks the same whether it was set as automatic or as an empty static
            // list (the registry has no entry in both cases), so two empty lists are always equal.
            var aDns = a.DnsMode == DnsMode.Static ? a.DnsServers : (IReadOnlyList<IPAddress>)new IPAddress[0];
            var bDns = b.DnsMode == DnsMode.Static ? b.DnsServers : (IReadOnlyList<IPAddress>)new IPAddress[0];
            if (!aDns.SequenceEqual(bDns))
            {
                return false;
            }

            if (a.AddressMode == AddressMode.Static)
            {
                if (a.Addresses.Count != b.Addresses.Count
                    || !a.Addresses.All(x => b.Addresses.Any(y => x.SameAs(y)))
                    || !Equals(a.Gateway, b.Gateway))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
