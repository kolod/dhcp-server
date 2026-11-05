using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace DhcpServer.Core.Addressing
{
    /// <summary>Text format of DhcpServer.restore (contracts/restore-file.md).</summary>
    public static class RestoreFile
    {
        public const string Header = "#DhcpServer restore v1";
        private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static string Serialize(SavedConfiguration record)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            sb.Append("created=").Append(record.CreatedUtc.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("adapter=").Append(record.AdapterId).Append("\r\n");
            sb.Append("index=").Append(record.Previous.InterfaceIndex.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("name=").Append(SingleLine(record.AdapterName)).Append("\r\n");
            Write(sb, "previous", record.Previous);
            Write(sb, "applied", record.Applied);
            if (record.Restoring)
            {
                sb.Append("restoring=1\r\n");
            }

            return sb.ToString();
        }

        public static SavedConfiguration Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new RestoreRecordCorruptException("empty");
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines[0].Trim() != Header)
            {
                throw new RestoreRecordCorruptException("header");
            }

            var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, eq).Trim();
                List<string> list;
                if (!values.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    values[key] = list;
                }

                list.Add(line.Substring(eq + 1).Trim());
            }

            var adapter = First(values, "adapter");
            if (string.IsNullOrEmpty(adapter) || First(values, "previous.mode") == null || First(values, "applied.mode") == null)
            {
                throw new RestoreRecordCorruptException("missing keys");
            }

            int index;
            int.TryParse(First(values, "index"), NumberStyles.None, CultureInfo.InvariantCulture, out index);

            DateTime created;
            if (!DateTime.TryParseExact(First(values, "created") ?? string.Empty, TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created))
            {
                created = DateTime.MinValue;
            }

            return new SavedConfiguration(
                First(values, "name"),
                Read(values, "previous", adapter, index),
                Read(values, "applied", adapter, index),
                created,
                First(values, "restoring") == "1");
        }

        private static void Write(StringBuilder sb, string prefix, AdapterConfig config)
        {
            sb.Append(prefix).Append(".mode=").Append(config.AddressMode == AddressMode.Automatic ? "dhcp" : "static").Append("\r\n");
            if (config.AddressMode == AddressMode.Static)
            {
                foreach (var a in config.Addresses)
                {
                    sb.Append(prefix).Append(".address=").Append(a.Address).Append('/')
                        .Append(a.PrefixLength.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                }

                sb.Append(prefix).Append(".gateway=").Append(config.Gateway == null ? "none" : config.Gateway.ToString()).Append("\r\n");
            }

            sb.Append(prefix).Append(".dnsmode=").Append(config.DnsMode == DnsMode.Automatic ? "dhcp" : "static").Append("\r\n");
            if (config.DnsMode == DnsMode.Static)
            {
                foreach (var d in config.DnsServers)
                {
                    sb.Append(prefix).Append(".dns=").Append(d).Append("\r\n");
                }
            }
        }

        private static AdapterConfig Read(Dictionary<string, List<string>> values, string prefix, string adapter, int index)
        {
            var mode = First(values, prefix + ".mode") == "dhcp" ? AddressMode.Automatic : AddressMode.Static;
            var dnsMode = First(values, prefix + ".dnsmode") == "dhcp" ? DnsMode.Automatic : DnsMode.Static;

            var addresses = new List<AdapterAddress>();
            foreach (var text in All(values, prefix + ".address"))
            {
                var slash = text.IndexOf('/');
                IPAddress ip;
                int length;
                if (slash <= 0 || !IPAddress.TryParse(text.Substring(0, slash), out ip)
                    || !int.TryParse(text.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out length)
                    || length < 0 || length > 32)
                {
                    throw new RestoreRecordCorruptException("address");
                }

                addresses.Add(new AdapterAddress(ip, length));
            }

            if (mode == AddressMode.Static && addresses.Count == 0)
            {
                throw new RestoreRecordCorruptException("no address");
            }

            IPAddress gateway = null;
            var gatewayText = First(values, prefix + ".gateway");
            if (!string.IsNullOrEmpty(gatewayText) && gatewayText != "none" && !IPAddress.TryParse(gatewayText, out gateway))
            {
                throw new RestoreRecordCorruptException("gateway");
            }

            var dns = new List<IPAddress>();
            foreach (var text in All(values, prefix + ".dns"))
            {
                IPAddress ip;
                if (!IPAddress.TryParse(text, out ip))
                {
                    throw new RestoreRecordCorruptException("dns");
                }

                dns.Add(ip);
            }

            return new AdapterConfig(adapter, index, addresses, gateway, dns, mode, dnsMode);
        }

        private static string First(Dictionary<string, List<string>> values, string key)
        {
            List<string> list;
            return values.TryGetValue(key, out list) && list.Count > 0 ? list[0] : null;
        }

        private static IEnumerable<string> All(Dictionary<string, List<string>> values, string key)
        {
            List<string> list;
            return values.TryGetValue(key, out list) ? list : (IEnumerable<string>)new string[0];
        }

        private static string SingleLine(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }
    }
}
