using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DhcpServer.Core.Abstractions;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Asks the local segment whether an address already answers: ARP (SendARP, Windows 7 SP1+) and an
    /// ICMP echo run in parallel and the first positive answer wins. SendARP has no timeout of its own
    /// (it can block for seconds), so we stop waiting after <see cref="BudgetMilliseconds"/>.
    /// </summary>
    internal sealed class ArpIcmpProber : IAddressProber
    {
        private const int BudgetMilliseconds = 400;
        private const int PingTimeoutMilliseconds = 300;

        public bool IsInUse(IPAddress address, IPAddress viaInterface)
        {
            try
            {
                var arp = Task.Run(() => ArpAnswers(address, viaInterface));
                var ping = Task.Run(() => PingAnswers(address));
                var pending = new List<Task<bool>> { arp, ping };
                var deadline = Environment.TickCount + BudgetMilliseconds;

                while (pending.Count > 0)
                {
                    var left = deadline - Environment.TickCount;
                    if (left <= 0)
                    {
                        return false;
                    }

                    var index = Task.WaitAny(pending.ToArray(), left);
                    if (index < 0)
                    {
                        return false;
                    }

                    var finished = pending[index];
                    pending.RemoveAt(index);
                    if (!finished.IsFaulted && finished.Result)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception)
            {
                // A failing probe must never block serving: treat as "not in use".
                return false;
            }
        }

        private static bool ArpAnswers(IPAddress destination, IPAddress source)
        {
            var mac = new byte[6];
            var length = (uint)mac.Length;
            var result = SendARP(ToNetworkOrder(destination), ToNetworkOrder(source), mac, ref length);
            return result == 0 && length > 0;
        }

        private static bool PingAnswers(IPAddress destination)
        {
            using (var ping = new Ping())
            {
                var reply = ping.Send(destination, PingTimeoutMilliseconds);
                return reply != null && reply.Status == IPStatus.Success;
            }
        }

        private static uint ToNetworkOrder(IPAddress address)
        {
            return BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destinationIp, uint sourceIp, byte[] macAddress, ref uint physicalAddressLength);
    }
}
