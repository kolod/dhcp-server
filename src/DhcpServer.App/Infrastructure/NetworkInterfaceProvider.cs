using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Network;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>Reads the PC's adapters through System.Net.NetworkInformation (available on Windows 7 SP1+).</summary>
    internal sealed class NetworkInterfaceProvider : IInterfaceProvider
    {
        public NetworkInterfaceProvider()
        {
            NetworkChange.NetworkAddressChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
            NetworkChange.NetworkAvailabilityChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Changed;

        public IReadOnlyList<InterfaceInfo> GetInterfaces()
        {
            var result = new List<InterfaceInfo>();
            NetworkInterface[] all;
            try
            {
                all = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                return result;
            }

            foreach (var ni in all)
            {
                try
                {
                    result.Add(Convert(ni));
                }
                catch (NetworkInformationException)
                {
                    // An adapter that disappears while being read is simply skipped.
                }
            }

            return result;
        }

        private static InterfaceInfo Convert(NetworkInterface ni)
        {
            var props = ni.GetIPProperties();
            var unicast = props.UnicastAddresses
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
            var chosen = unicast.FirstOrDefault(u => !IpMath.IsLinkLocal(u.Address)) ?? unicast.FirstOrDefault();

            IPAddress address = null;
            var prefix = 0;
            if (chosen != null)
            {
                address = chosen.Address;
                prefix = chosen.PrefixLength;
                if (prefix == 0 && chosen.IPv4Mask != null)
                {
                    prefix = CountBits(IpMath.ToUInt32(chosen.IPv4Mask));
                }
            }

            var gateway = props.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));

            return new InterfaceInfo(
                ni.Id,
                ni.Name,
                MapKind(ni.NetworkInterfaceType),
                ni.OperationalStatus == OperationalStatus.Up,
                address,
                prefix,
                gateway,
                ni.GetPhysicalAddress().GetAddressBytes());
        }

        private static int CountBits(uint mask)
        {
            var count = 0;
            for (; mask != 0; mask <<= 1)
            {
                count++;
            }

            return count;
        }

        private static InterfaceKind MapKind(NetworkInterfaceType type)
        {
            switch (type)
            {
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.Ethernet3Megabit:
                case NetworkInterfaceType.FastEthernetT:
                case NetworkInterfaceType.FastEthernetFx:
                case NetworkInterfaceType.GigabitEthernet:
                    return InterfaceKind.Ethernet;
                case NetworkInterfaceType.Wireless80211:
                    return InterfaceKind.Wireless;
                case NetworkInterfaceType.Loopback:
                    return InterfaceKind.Loopback;
                case NetworkInterfaceType.Tunnel:
                    return InterfaceKind.Tunnel;
                default:
                    return InterfaceKind.Other;
            }
        }
    }
}
