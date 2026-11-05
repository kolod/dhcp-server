using System;
using System.Collections.Generic;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Abstractions
{
    /// <summary>Enumerates the PC's network adapters and reports when they change.</summary>
    public interface IInterfaceProvider
    {
        IReadOnlyList<InterfaceInfo> GetInterfaces();

        /// <summary>Raised (possibly on a background thread) when adapters or addresses change.</summary>
        event EventHandler Changed;
    }
}
