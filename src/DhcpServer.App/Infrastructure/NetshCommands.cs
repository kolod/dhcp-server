using System.Collections.Generic;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Network;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Builds the exact <c>netsh interface ipv4</c> argument lists of research D2. Arguments contain only
    /// numbers and validated IPv4 addresses — no user text ever reaches a command line.
    /// </summary>
    internal static class NetshCommands
    {
        public static IReadOnlyList<string> Build(ApplyStep step, AdapterConfig target, int addressIndex, int interfaceIndex)
        {
            var name = "name=" + interfaceIndex;
            var commands = new List<string>();
            switch (step)
            {
                case ApplyStep.SetAddress:
                {
                    var address = target.Addresses[0];
                    commands.Add("interface ipv4 set address " + name + " source=static address=" + address.Address
                        + " mask=" + IpMath.MaskAddress(address.PrefixLength)
                        + " gateway=" + (target.Gateway == null ? "none" : target.Gateway.ToString()));
                    break;
                }

                case ApplyStep.AddAddress:
                {
                    var address = target.Addresses[addressIndex];
                    commands.Add("interface ipv4 add address " + name + " address=" + address.Address
                        + " mask=" + IpMath.MaskAddress(address.PrefixLength));
                    break;
                }

                case ApplyStep.SetDns:
                    commands.Add("interface ipv4 set dnsservers " + name + " source=static address=" + target.DnsServers[0] + " validate=no");
                    for (var i = 1; i < target.DnsServers.Count; i++)
                    {
                        commands.Add("interface ipv4 add dnsservers " + name + " address=" + target.DnsServers[i] + " validate=no");
                    }

                    break;
                case ApplyStep.ClearDns:
                    commands.Add("interface ipv4 set dnsservers " + name + " source=static address=none validate=no");
                    break;
                case ApplyStep.SetAddressDhcp:
                    commands.Add("interface ipv4 set address " + name + " source=dhcp");
                    break;
                case ApplyStep.SetDnsDhcp:
                    commands.Add("interface ipv4 set dnsservers " + name + " source=dhcp");
                    break;
            }

            return commands;
        }
    }
}
