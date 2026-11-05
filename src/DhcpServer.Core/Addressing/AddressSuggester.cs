using System.Collections.Generic;
using System.Linq;
using System.Net;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Addressing
{
    /// <summary>Picks a private /24 for the session that does not overlap anything already connected (research D9).</summary>
    public static class AddressSuggester
    {
        public const int PrefixLength = 24;

        public static IEnumerable<IPAddress> Candidates()
        {
            yield return IPAddress.Parse("192.168.137.1");
            for (var third = 138; third <= 199; third++)
            {
                yield return IPAddress.Parse("192.168." + third + ".1");
            }

            yield return IPAddress.Parse("10.137.0.1");
        }

        /// <param name="prober">Optional: a candidate whose address already answers is skipped.</param>
        public static AddressSettings Suggest(string chosenInterfaceId, IEnumerable<InterfaceInfo> all, IAddressProber prober = null)
        {
            var interfaces = all.ToList();
            foreach (var candidate in Candidates())
            {
                if (AddressSettingsValidator.Overlaps(candidate, PrefixLength, chosenInterfaceId, interfaces))
                {
                    continue;
                }

                if (prober != null && prober.IsInUse(candidate, IPAddress.Any))
                {
                    continue;
                }

                return new AddressSettings(candidate, PrefixLength, null, null);
            }

            return null;
        }
    }
}
