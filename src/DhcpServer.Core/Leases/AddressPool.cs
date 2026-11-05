using System.Collections.Generic;
using System.Net;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Leases
{
    /// <summary>
    /// The addresses that may be handed out on the chosen interface: the subnet's host addresses
    /// minus the server's own address and the gateway, limited to a window of <see cref="WindowSize"/>.
    /// </summary>
    public sealed class AddressPool
    {
        public const int WindowSize = 254;

        private readonly HashSet<uint> _members = new HashSet<uint>();

        public AddressPool(IPAddress server, int prefixLength, IPAddress gateway)
        {
            Server = server;
            PrefixLength = prefixLength;
            Gateway = gateway;

            var network = IpMath.Network(server, prefixLength);
            var broadcast = IpMath.Broadcast(server, prefixLength);
            Network = IpMath.FromUInt32(network);
            Broadcast = IpMath.FromUInt32(broadcast);
            Mask = IpMath.MaskAddress(prefixLength);

            var serverValue = IpMath.ToUInt32(server);
            var gatewayInSubnet = gateway != null && IpMath.IsInSubnet(gateway, server, prefixLength);
            var gatewayValue = gatewayInSubnet ? IpMath.ToUInt32(gateway) : 0u;

            var list = new List<IPAddress>();
            for (ulong a = (ulong)network + 1; a < broadcast && list.Count < WindowSize; a++)
            {
                var value = (uint)a;
                if (value == serverValue || (gatewayInSubnet && value == gatewayValue))
                {
                    continue;
                }

                list.Add(IpMath.FromUInt32(value));
                _members.Add(value);
            }

            Addresses = list;
        }

        public IPAddress Server { get; }

        public IPAddress Gateway { get; }

        public int PrefixLength { get; }

        public IPAddress Network { get; }

        public IPAddress Mask { get; }

        public IPAddress Broadcast { get; }

        public IReadOnlyList<IPAddress> Addresses { get; }

        public bool Contains(IPAddress address)
        {
            return address != null && address.GetAddressBytes().Length == 4 && _members.Contains(IpMath.ToUInt32(address));
        }

        public bool IsSameSubnet(IPAddress network, int prefixLength)
        {
            return prefixLength == PrefixLength && IpMath.ToUInt32(network) == IpMath.ToUInt32(Network);
        }
    }
}
