using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DhcpServer.Core.Settings
{
    /// <summary>
    /// Minimal flat "key=value" settings document. Unknown lines are kept, so a file edited by
    /// hand or by a newer version survives a save. Keys are case-insensitive.
    /// </summary>
    public sealed class IniDocument
    {
        private readonly List<string> _lines = new List<string>();

        public static IniDocument Parse(string text)
        {
            var doc = new IniDocument();
            if (!string.IsNullOrEmpty(text))
            {
                using (var reader = new StringReader(text))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        doc._lines.Add(line);
                    }
                }
            }

            return doc;
        }

        public string Get(string key)
        {
            foreach (var line in _lines)
            {
                string k, v;
                if (TrySplit(line, out k, out v) && Matches(k, key))
                {
                    return v;
                }
            }

            return null;
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || key.IndexOf('=') >= 0)
            {
                throw new ArgumentException("Invalid key.", nameof(key));
            }

            var entry = key.Trim() + "=" + (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            for (var i = 0; i < _lines.Count; i++)
            {
                string k, v;
                if (TrySplit(_lines[i], out k, out v) && Matches(k, key))
                {
                    _lines[i] = entry;
                    return;
                }
            }

            _lines.Add(entry);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var line in _lines)
            {
                sb.Append(line).Append("\r\n");
            }

            return sb.ToString();
        }

        private static bool TrySplit(string line, out string key, out string value)
        {
            key = null;
            value = null;
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#' || trimmed[0] == '[')
            {
                return false;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }

            key = line.Substring(0, eq).Trim();
            value = line.Substring(eq + 1).Trim();
            return key.Length > 0;
        }

        private static bool Matches(string a, string b)
        {
            return string.Equals(a, b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
