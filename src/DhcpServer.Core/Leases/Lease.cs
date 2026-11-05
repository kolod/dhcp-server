using System;
using System.Net;
using System.Text;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Leases
{
    public enum LeaseState
    {
        Active,
        Expired,
        Released
    }

    /// <summary>An address assigned to a device (identified by MAC) for a limited time. Immutable.</summary>
    public sealed class Lease
    {
        public const int MaxHostNameLength = 63;

        public Lease(string mac, IPAddress address, string hostName, DateTime start, DateTime expires, bool isReleased, int prefixLength)
        {
            Mac = mac;
            Address = address;
            HostName = SanitizeHostName(hostName);
            Start = start;
            Expires = expires;
            IsReleased = isReleased;
            PrefixLength = prefixLength;
            Network = IpMath.FromUInt32(IpMath.Network(address, prefixLength));
        }

        /// <summary>12 lowercase hex digits, see <see cref="MacAddress"/>.</summary>
        public string Mac { get; }

        public IPAddress Address { get; }

        public string HostName { get; }

        public DateTime Start { get; }

        public DateTime Expires { get; }

        public bool IsReleased { get; }

        /// <summary>Subnet the lease was issued in.</summary>
        public IPAddress Network { get; }

        public int PrefixLength { get; }

        public LeaseState GetState(DateTime now)
        {
            if (IsReleased)
            {
                return LeaseState.Released;
            }

            return now >= Expires ? LeaseState.Expired : LeaseState.Active;
        }

        public bool IsActive(DateTime now)
        {
            return GetState(now) == LeaseState.Active;
        }

        public Lease AsReleased()
        {
            return new Lease(Mac, Address, HostName, Start, Expires, true, PrefixLength);
        }

        public static string SanitizeHostName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var c in value)
            {
                if (c >= 0x20 && c <= 0x7E)
                {
                    sb.Append(c);
                }
            }

            var text = sb.ToString().Trim();
            return text.Length > MaxHostNameLength ? text.Substring(0, MaxHostNameLength) : text;
        }
    }

    public static class MacAddress
    {
        /// <summary>Canonical key: 12 lowercase hex digits.</summary>
        public static string ToKey(byte[] mac)
        {
            var sb = new StringBuilder(12);
            for (var i = 0; i < 6 && i < mac.Length; i++)
            {
                sb.Append(mac[i].ToString("x2"));
            }

            return sb.ToString();
        }

        public static bool IsValidKey(string key)
        {
            if (key == null || key.Length != 12)
            {
                return false;
            }

            foreach (var c in key)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Display form, e.g. A1:B2:C3:D4:E5:F6.</summary>
        public static string Format(string key)
        {
            if (!IsValidKey(key))
            {
                return key ?? string.Empty;
            }

            var sb = new StringBuilder(17);
            for (var i = 0; i < 12; i += 2)
            {
                if (i > 0)
                {
                    sb.Append(':');
                }

                sb.Append(key, i, 2);
            }

            return sb.ToString().ToUpperInvariant();
        }
    }
}
