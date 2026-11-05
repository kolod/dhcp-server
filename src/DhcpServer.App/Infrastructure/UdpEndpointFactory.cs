using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Runtime;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Binds UDP 67 on exactly one local address (never 0.0.0.0): the socket then only sees traffic
    /// that arrives on that interface (research D1).
    /// </summary>
    internal sealed class UdpEndpointFactory : IEndpointFactory
    {
        private const int ServerPort = 67;
        private const int ClientPort = 68;

        public IDhcpEndpoint Bind(IPAddress localAddress)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.ExclusiveAddressUse = true;
                socket.EnableBroadcast = true;
                socket.Bind(new IPEndPoint(localAddress, ServerPort));
            }
            catch (SocketException e)
            {
                socket.Close();
                throw new EndpointBindException(Map(e), e);
            }
            catch (Exception e)
            {
                socket.Close();
                throw new EndpointBindException(new FailureReason(FailureKind.Unexpected, e.Message), e);
            }

            return new UdpEndpoint(socket);
        }

        private static FailureReason Map(SocketException e)
        {
            switch (e.SocketErrorCode)
            {
                case SocketError.AddressAlreadyInUse:
                    return new FailureReason(FailureKind.PortInUse);
                case SocketError.AccessDenied:
                    // With an exclusive bind Windows reports "access denied" when another program holds the port.
                    return new FailureReason(IsElevated() ? FailureKind.PortInUse : FailureKind.AccessDenied);
                default:
                    return new FailureReason(FailureKind.Unexpected, e.Message);
            }
        }

        private static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class UdpEndpoint : IDhcpEndpoint
        {
            private readonly Socket _socket;
            private readonly byte[] _buffer = new byte[2048];
            private volatile bool _closed;

            public UdpEndpoint(Socket socket)
            {
                _socket = socket;
            }

            public bool TryReceive(out byte[] data)
            {
                while (!_closed)
                {
                    try
                    {
                        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        var count = _socket.ReceiveFrom(_buffer, ref remote);
                        data = new byte[count];
                        Array.Copy(_buffer, data, count);
                        return true;
                    }
                    catch (SocketException e)
                    {
                        // An ICMP "port unreachable" for an earlier reply surfaces as ConnectionReset: ignore.
                        if (_closed || e.SocketErrorCode != SocketError.ConnectionReset)
                        {
                            break;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }

                data = null;
                return false;
            }

            public void SendBroadcast(byte[] data)
            {
                _socket.SendTo(data, new IPEndPoint(IPAddress.Broadcast, ClientPort));
            }

            public void Close()
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                try
                {
                    _socket.Close();
                }
                catch (SocketException)
                {
                }
            }

            public void Dispose()
            {
                Close();
            }
        }
    }
}
