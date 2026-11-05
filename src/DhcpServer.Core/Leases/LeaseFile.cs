using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Leases
{
    /// <summary>Text format of DhcpServer.leases (see contracts/lease-file.md).</summary>
    public static class LeaseFile
    {
        public const string Header = "#DhcpServer leases v1";
        public const int MaxEntries = 1024;
        private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static IReadOnlyList<Lease> Parse(string text)
        {
            var result = new List<Lease>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header)
            {
                return result;
            }

            var byMac = new Dictionary<string, Lease>();
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var lease = ParseLine(raw);
                if (lease == null)
                {
                    continue;
                }

                Lease existing;
                if (!byMac.TryGetValue(lease.Mac, out existing) || existing.Expires < lease.Expires)
                {
                    byMac[lease.Mac] = lease;
                }
            }

            // Duplicate addresses: the later expiry wins.
            foreach (var lease in byMac.Values.OrderByDescending(l => l.Expires))
            {
                if (!result.Any(r => r.Address.Equals(lease.Address)))
                {
                    result.Add(lease);
                }
            }

            return result;
        }

        public static string Serialize(IEnumerable<Lease> leases, DateTime now)
        {
            var list = leases.ToList();
            if (list.Count > MaxEntries)
            {
                list = list
                    .OrderBy(l => l.IsActive(now) ? 0 : 1)
                    .ThenByDescending(l => l.Expires)
                    .Take(MaxEntries)
                    .ToList();
            }

            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            foreach (var l in list.OrderBy(l => IpMath.ToUInt32(l.Address)))
            {
                sb.Append(l.Mac).Append('\t')
                    .Append(l.Address).Append('\t')
                    .Append(l.PrefixLength.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(l.Start.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture)).Append('\t')
                    .Append(l.Expires.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture)).Append('\t')
                    .Append(l.IsReleased ? 'R' : 'A').Append('\t')
                    .Append(l.HostName)
                    .Append("\r\n");
            }

            return sb.ToString();
        }

        private static Lease ParseLine(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length != 6 && parts.Length != 7)
            {
                return null;
            }

            var mac = parts[0].Trim().ToLowerInvariant();
            if (!MacAddress.IsValidKey(mac))
            {
                return null;
            }

            IPAddress ip;
            if (parts[1].Split('.').Length != 4 || !IPAddress.TryParse(parts[1], out ip))
            {
                return null;
            }

            int prefix;
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out prefix) || prefix < 8 || prefix > 30)
            {
                return null;
            }

            DateTime start, expires;
            const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
            if (!DateTime.TryParseExact(parts[3], TimeFormat, CultureInfo.InvariantCulture, styles, out start)
                || !DateTime.TryParseExact(parts[4], TimeFormat, CultureInfo.InvariantCulture, styles, out expires))
            {
                return null;
            }

            var state = parts[5].Trim();
            if (state != "A" && state != "R")
            {
                return null;
            }

            return new Lease(mac, ip, parts.Length == 7 ? parts[6] : string.Empty, start, expires, state == "R", prefix);
        }
    }
}
