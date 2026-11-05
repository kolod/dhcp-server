using System;

namespace DhcpServer.Core.Abstractions
{
    /// <summary>A UDP endpoint bound to the DHCP server port on exactly one local address.</summary>
    public interface IDhcpEndpoint : IDisposable
    {
        /// <summary>Blocks until a datagram arrives; returns false once the endpoint is closed.</summary>
        bool TryReceive(out byte[] data);

        /// <summary>Sends a datagram to the limited broadcast address on the client port.</summary>
        void SendBroadcast(byte[] data);

        /// <summary>Closes the endpoint and unblocks <see cref="TryReceive"/>. Idempotent.</summary>
        void Close();
    }
}
