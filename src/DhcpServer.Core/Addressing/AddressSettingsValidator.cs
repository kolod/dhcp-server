using System.Collections.Generic;
using System.Linq;
using System.Net;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Addressing
{
    public enum AddressField
    {
        Address,
        Mask,
        Gateway,
        Dns1,
        Dns2
    }

    public enum FieldErrorKind
    {
        Required,
        Malformed,
        NotUnicast,
        LinkLocal,
        MaskInvalid,
        MaskSize,
        NetworkOrBroadcast,
        GatewayOutsideSubnet,
        DnsDuplicate,
        SubnetOverlap
    }

    public sealed class FieldError
    {
        public FieldError(AddressField field, FieldErrorKind kind)
        {
            Field = field;
            Kind = kind;
        }

        public AddressField Field { get; }

        public FieldErrorKind Kind { get; }

        /// <summary>String key of the inline message.</summary>
        public string ResourceKey => "Field_" + Kind;
    }

    /// <summary>The raw text of the entry fields (the view model passes what the user typed).</summary>
    public sealed class AddressFieldInput
    {
        public string Address { get; set; }

        public string Mask { get; set; }

        public string Gateway { get; set; }

        public string Dns1 { get; set; }

        public string Dns2 { get; set; }
    }

    public sealed class ValidationResult
    {
        public ValidationResult(IReadOnlyList<FieldError> errors, AddressSettings settings)
        {
            Errors = errors;
            Settings = settings;
        }

        public IReadOnlyList<FieldError> Errors { get; }

        public bool IsValid => Errors.Count == 0;

        /// <summary>The parsed settings; null when there are errors.</summary>
        public AddressSettings Settings { get; }
    }

    /// <summary>Rules of research D9 (pure, no I/O).</summary>
    public static class AddressSettingsValidator
    {
        public static ValidationResult Validate(AddressFieldInput input, string chosenInterfaceId, IEnumerable<InterfaceInfo> all)
        {
            var errors = new List<FieldError>();

            IPAddress address;
            var addressOk = ParseUnicast(input.Address, AddressField.Address, true, errors, out address);

            IPAddress maskAddress;
            int prefix = 0;
            var maskOk = ParseMask(input.Mask, errors, out maskAddress, out prefix);

            if (addressOk && maskOk)
            {
                var value = IpMath.ToUInt32(address);
                if (value == IpMath.Network(address, prefix) || value == IpMath.Broadcast(address, prefix))
                {
                    errors.Add(new FieldError(AddressField.Address, FieldErrorKind.NetworkOrBroadcast));
                    addressOk = false;
                }
                else if (Overlaps(address, prefix, chosenInterfaceId, all))
                {
                    errors.Add(new FieldError(AddressField.Address, FieldErrorKind.SubnetOverlap));
                    addressOk = false;
                }
            }

            IPAddress gateway = null;
            if (!string.IsNullOrWhiteSpace(input.Gateway))
            {
                if (ParseUnicast(input.Gateway, AddressField.Gateway, false, errors, out gateway) && addressOk && maskOk)
                {
                    var g = IpMath.ToUInt32(gateway);
                    if (!IpMath.IsInSubnet(gateway, address, prefix)
                        || g == IpMath.Network(address, prefix)
                        || g == IpMath.Broadcast(address, prefix)
                        || gateway.Equals(address))
                    {
                        errors.Add(new FieldError(AddressField.Gateway, FieldErrorKind.GatewayOutsideSubnet));
                        gateway = null;
                    }
                }
            }

            var dns = new List<IPAddress>();
            IPAddress dns1, dns2;
            if (!string.IsNullOrWhiteSpace(input.Dns1) && ParseUnicast(input.Dns1, AddressField.Dns1, false, errors, out dns1))
            {
                dns.Add(dns1);
            }

            if (!string.IsNullOrWhiteSpace(input.Dns2) && ParseUnicast(input.Dns2, AddressField.Dns2, false, errors, out dns2))
            {
                if (dns.Contains(dns2))
                {
                    errors.Add(new FieldError(AddressField.Dns2, FieldErrorKind.DnsDuplicate));
                }
                else
                {
                    dns.Add(dns2);
                }
            }

            if (errors.Count > 0)
            {
                return new ValidationResult(errors, null);
            }

            return new ValidationResult(errors, new AddressSettings(address, prefix, gateway, dns));
        }

        private static bool ParseUnicast(string text, AddressField field, bool required, List<FieldError> errors, out IPAddress result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                if (required)
                {
                    errors.Add(new FieldError(field, FieldErrorKind.Required));
                }

                return false;
            }

            if (!TryParseIPv4(text.Trim(), out result))
            {
                errors.Add(new FieldError(field, FieldErrorKind.Malformed));
                return false;
            }

            var first = result.GetAddressBytes()[0];
            if (first == 0 || first == 127 || first >= 224 || result.Equals(IPAddress.Broadcast))
            {
                errors.Add(new FieldError(field, FieldErrorKind.NotUnicast));
                return false;
            }

            if (IpMath.IsLinkLocal(result))
            {
                errors.Add(new FieldError(field, FieldErrorKind.LinkLocal));
                return false;
            }

            return true;
        }

        private static bool ParseMask(string text, List<FieldError> errors, out IPAddress mask, out int prefix)
        {
            mask = null;
            prefix = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                errors.Add(new FieldError(AddressField.Mask, FieldErrorKind.Required));
                return false;
            }

            if (!TryParseIPv4(text.Trim(), out mask))
            {
                errors.Add(new FieldError(AddressField.Mask, FieldErrorKind.Malformed));
                return false;
            }

            var value = IpMath.ToUInt32(mask);
            var bits = 0;
            for (var v = value; (v & 0x80000000u) != 0; v <<= 1)
            {
                bits++;
            }

            if (IpMath.Mask(bits) != value)
            {
                errors.Add(new FieldError(AddressField.Mask, FieldErrorKind.MaskInvalid));
                return false;
            }

            if (bits < InterfaceEligibility.MinPrefixLength || bits > InterfaceEligibility.MaxPrefixLength)
            {
                errors.Add(new FieldError(AddressField.Mask, FieldErrorKind.MaskSize));
                return false;
            }

            prefix = bits;
            return true;
        }

        private static bool TryParseIPv4(string text, out IPAddress address)
        {
            address = null;
            var parts = text.Split('.');
            if (parts.Length != 4 || parts.Any(p => p.Length == 0 || p.Length > 3 || !p.All(char.IsDigit)))
            {
                return false;
            }

            return IPAddress.TryParse(text, out address);
        }

        /// <summary>True when the proposed subnet overlaps another active, non-loopback interface.</summary>
        public static bool Overlaps(IPAddress address, int prefix, string chosenInterfaceId, IEnumerable<InterfaceInfo> all)
        {
            foreach (var other in all)
            {
                if (other.Id == chosenInterfaceId || !other.IsUp || other.Kind == InterfaceKind.Loopback
                    || other.Address == null || IpMath.IsLoopback(other.Address))
                {
                    continue;
                }

                if (IpMath.Overlaps(address, prefix, other.Address, other.PrefixLength))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
