using System.Net;

namespace DhcpServer.Core.Abstractions
{
    /// <summary>Checks whether an address already answers on the local segment (ARP / ping).</summary>
    public interface IAddressProber
    {
        bool IsInUse(IPAddress address, IPAddress viaInterface);
    }
}
