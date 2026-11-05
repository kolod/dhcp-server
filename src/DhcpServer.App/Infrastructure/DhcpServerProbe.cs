using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Dhcp;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Looks for another DHCP server before we start serving: one DISCOVER from a random locally
    /// administered MAC, listening for any OFFER for 3 s, twice (research D3). No lease is ever
    /// requested, so there is nothing to release. If the client port cannot be bound the result is
    /// "inconclusive" and the start is refused — the check is never silently skipped.
    /// </summary>
    internal sealed class DhcpServerProbe : IDhcpServerProbe
    {
        private const int ClientPort = 68;
        private const int ServerPort = 67;
        private const int Rounds = 2;
        private const int RoundMilliseconds = 3000;

        public ProbeResult Probe(IPAddress viaInterface, CancellationToken cancellation)
        {
            Socket socket;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.EnableBroadcast = true;
                socket.Bind(new IPEndPoint(viaInterface, ClientPort));
            }
            catch (SocketException)
            {
                return ProbeResult.Inconclusive;
            }

            using (socket)
            {
                var random = new Random();
                var mac = new byte[6];
                random.NextBytes(mac);
                mac[0] = (byte)((mac[0] & 0xFE) | 0x02); // locally administered, unicast

                var xid = (uint)random.Next();
                var discover = new DhcpMessage { Op = 1, Xid = xid, Flags = 0x8000, ClientMac = mac };
                discover.SetByteOption(DhcpOptionCode.MessageType, (byte)DhcpMessageType.Discover);
                discover.Options[DhcpOptionCode.ParameterRequestList] = new byte[] { DhcpOptionCode.SubnetMask, DhcpOptionCode.Router };
                var packet = discover.ToBytes();

                for (var round = 0; round < Rounds; round++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        socket.SendTo(packet, new IPEndPoint(IPAddress.Broadcast, ServerPort));
                    }
                    catch (SocketException)
                    {
                        return ProbeResult.Inconclusive;
                    }

                    var found = Listen(socket, cancellation);
                    if (found != null)
                    {
                        return ProbeResult.ServerFound(found);
                    }
                }
            }

            return ProbeResult.NoServer;
        }

        private static IPAddress Listen(Socket socket, CancellationToken cancellation)
        {
            var buffer = new byte[2048];
            var until = Environment.TickCount + RoundMilliseconds;
            while (Environment.TickCount - until < 0)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (!socket.Poll(100 * 1000, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    var count = socket.ReceiveFrom(buffer, ref remote);
                    var data = new byte[count];
                    Array.Copy(buffer, data, count);

                    DhcpMessage message;
                    if (DhcpMessage.TryParse(data, out message) && message.Op == 2 && message.MessageType == DhcpMessageType.Offer)
                    {
                        return message.GetAddressOption(DhcpOptionCode.ServerIdentifier) ?? ((IPEndPoint)remote).Address;
                    }
                }
                catch (SocketException)
                {
                    // e.g. connection reset from an ICMP error: keep listening
                }
            }

            return null;
        }
    }
}
