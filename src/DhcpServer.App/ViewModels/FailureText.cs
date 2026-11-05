using System.Linq;
using DhcpServer.App.Localization;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Runtime;

namespace DhcpServer.App.ViewModels
{
    /// <summary>Turns failure reasons and saved configurations into localized text.</summary>
    internal static class FailureText
    {
        public static string Format(FailureReason reason)
        {
            switch (reason.Kind)
            {
                case FailureKind.AddressApplyFailed:
                    return LanguageService.Format(reason.ResourceKey, Step(reason.Argument));
                case FailureKind.AddressRestoreFailed:
                    return LanguageService.Format(reason.ResourceKey, Step(reason.Argument), Describe(reason.Previous));
                default:
                    return LanguageService.Format(reason.ResourceKey, reason.Argument ?? string.Empty);
            }
        }

        public static string Step(string stepName)
        {
            return string.IsNullOrEmpty(stepName) ? string.Empty : LanguageService.Get("Step_" + stepName);
        }

        /// <summary>A short readable description of an adapter configuration, for messages the user may act on by hand.</summary>
        public static string Describe(AdapterConfig config)
        {
            if (config == null)
            {
                return string.Empty;
            }

            var none = LanguageService.Get("Confirm_None");
            var dns = config.DnsMode == DnsMode.Static && config.DnsServers.Count > 0
                ? string.Join(", ", config.DnsServers.Select(d => d.ToString()))
                : null;

            if (config.AddressMode == AddressMode.Automatic)
            {
                var text = LanguageService.Get("Confirm_Automatic");
                return dns == null ? text : text + "; " + LanguageService.Get("Address_Dns") + ": " + dns;
            }

            return string.Join(", ", config.Addresses.Select(a => a.Address + "/" + a.PrefixLength))
                + "; " + LanguageService.Get("Address_Gateway") + ": " + (config.Gateway == null ? none : config.Gateway.ToString())
                + "; " + LanguageService.Get("Address_Dns") + ": "
                + (config.DnsMode == DnsMode.Automatic ? LanguageService.Get("Confirm_Automatic") : dns ?? none);
        }
    }
}
