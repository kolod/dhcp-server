using System;
using System.Net;

namespace DhcpServer.Core.Network
{
    /// <summary>IPv4 arithmetic on host-order unsigned integers.</summary>
    public static class IpMath
    {
        public static uint ToUInt32(IPAddress address)
        {
            var b = address.GetAddressBytes();
            if (b.Length != 4)
            {
                throw new ArgumentException("IPv4 address expected", nameof(address));
            }

            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        public static IPAddress FromUInt32(uint value)
        {
            return new IPAddress(new[]
            {
                (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
            });
        }

        public static uint Mask(int prefixLength)
        {
            if (prefixLength < 0 || prefixLength > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(prefixLength));
            }

            return prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        }

        public static IPAddress MaskAddress(int prefixLength)
        {
            return FromUInt32(Mask(prefixLength));
        }

        public static uint Network(IPAddress address, int prefixLength)
        {
            return ToUInt32(address) & Mask(prefixLength);
        }

        public static uint Broadcast(IPAddress address, int prefixLength)
        {
            return Network(address, prefixLength) | ~Mask(prefixLength);
        }

        public static bool IsInSubnet(IPAddress address, IPAddress networkMember, int prefixLength)
        {
            return Network(address, prefixLength) == Network(networkMember, prefixLength);
        }

        /// <summary>True when the two subnets share at least one address.</summary>
        public static bool Overlaps(IPAddress a, int prefixA, IPAddress b, int prefixB)
        {
            var common = Math.Min(prefixA, prefixB);
            return Network(a, common) == Network(b, common);
        }

        public static bool IsLinkLocal(IPAddress address)
        {
            var b = address.GetAddressBytes();
            return b.Length == 4 && b[0] == 169 && b[1] == 254;
        }

        public static bool IsLoopback(IPAddress address)
        {
            var b = address.GetAddressBytes();
            return b.Length == 4 && b[0] == 127;
        }
    }
}
