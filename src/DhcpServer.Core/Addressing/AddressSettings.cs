using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace DhcpServer.Core.Addressing
{
    /// <summary>What the user wants for the adapter during a session.</summary>
    public sealed class AddressSettings
    {
        public AddressSettings(IPAddress address, int prefixLength, IPAddress gateway, IEnumerable<IPAddress> dnsServers)
        {
            Address = address;
            PrefixLength = prefixLength;
            Gateway = gateway;
            DnsServers = (dnsServers ?? Enumerable.Empty<IPAddress>()).ToList();
        }

        public IPAddress Address { get; }

        public int PrefixLength { get; }

        /// <summary>Optional default gateway; null means none.</summary>
        public IPAddress Gateway { get; }

        /// <summary>0–2 DNS servers.</summary>
        public IReadOnlyList<IPAddress> DnsServers { get; }
    }
}
