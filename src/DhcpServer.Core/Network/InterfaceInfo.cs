using System.Net;

namespace DhcpServer.Core.Network
{
    public enum InterfaceKind
    {
        Ethernet,
        Wireless,
        Loopback,
        Tunnel,
        Other
    }

    /// <summary>Snapshot of one network adapter.</summary>
    public sealed class InterfaceInfo
    {
        public InterfaceInfo(
            string id,
            string name,
            InterfaceKind kind,
            bool isUp,
            IPAddress address,
            int prefixLength,
            IPAddress gateway = null,
            byte[] macAddress = null)
        {
            Id = id;
            Name = name;
            Kind = kind;
            IsUp = isUp;
            Address = address;
            PrefixLength = prefixLength;
            Gateway = gateway;
            MacAddress = macAddress;
        }

        public string Id { get; }

        public string Name { get; }

        public InterfaceKind Kind { get; }

        public bool IsUp { get; }

        /// <summary>Primary unicast IPv4 address, or null.</summary>
        public IPAddress Address { get; }

        public int PrefixLength { get; }

        public IPAddress Gateway { get; }

        public byte[] MacAddress { get; }
    }
}
