using System.Collections.Generic;
using System.Linq;

namespace DhcpServer.Core.Addressing
{
    public enum PreviewKind
    {
        Text,
        Automatic,
        Static,
        None
    }

    /// <summary>A value in the preview; markers (not text) so Core stays free of localized strings.</summary>
    public sealed class PreviewValue
    {
        public PreviewValue(PreviewKind kind, string text = null)
        {
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public PreviewKind Kind { get; }

        public string Text { get; }

        public bool SameAs(PreviewValue other)
        {
            return other != null && Kind == other.Kind && Text == other.Text;
        }
    }

    public sealed class PreviewRow
    {
        public PreviewRow(string labelKey, PreviewValue current, PreviewValue proposed)
        {
            LabelKey = labelKey;
            Current = current;
            New = proposed;
            Changed = !current.SameAs(proposed);
        }

        public string LabelKey { get; }

        public PreviewValue Current { get; }

        public PreviewValue New { get; }

        public bool Changed { get; }
    }

    /// <summary>The side-by-side comparison shown before anything is changed (FR-004).</summary>
    public sealed class ChangePreview
    {
        private ChangePreview(IReadOnlyList<PreviewRow> rows)
        {
            Rows = rows;
        }

        public IReadOnlyList<PreviewRow> Rows { get; }

        public static ChangePreview Create(AdapterConfig current, AddressSettings proposed)
        {
            var target = AdapterConfig.FromSettings(current.AdapterId, current.InterfaceIndex, proposed);
            var rows = new List<PreviewRow>
            {
                new PreviewRow("Confirm_Mode", Mode(current), Mode(target)),
                new PreviewRow("Address_Address", Join(current.Addresses.Select(a => a.Address.ToString())), Join(target.Addresses.Select(a => a.Address.ToString()))),
                new PreviewRow("Address_Mask", Join(current.Addresses.Select(a => IpMathMask(a.PrefixLength))), Join(target.Addresses.Select(a => IpMathMask(a.PrefixLength)))),
                new PreviewRow("Address_Gateway", Single(current.Gateway), Single(target.Gateway)),
                new PreviewRow("Address_Dns", Dns(current), Dns(target))
            };
            return new ChangePreview(rows);
        }

        private static string IpMathMask(int prefix)
        {
            return Network.IpMath.MaskAddress(prefix).ToString();
        }

        private static PreviewValue Mode(AdapterConfig config)
        {
            return new PreviewValue(config.AddressMode == AddressMode.Automatic ? PreviewKind.Automatic : PreviewKind.Static);
        }

        private static PreviewValue Join(IEnumerable<string> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? new PreviewValue(PreviewKind.None) : new PreviewValue(PreviewKind.Text, string.Join(", ", list));
        }

        private static PreviewValue Single(System.Net.IPAddress address)
        {
            return address == null ? new PreviewValue(PreviewKind.None) : new PreviewValue(PreviewKind.Text, address.ToString());
        }

        private static PreviewValue Dns(AdapterConfig config)
        {
            if (config.DnsMode == DnsMode.Automatic)
            {
                return new PreviewValue(PreviewKind.Automatic);
            }

            return Join(config.DnsServers.Select(d => d.ToString()));
        }
    }
}
