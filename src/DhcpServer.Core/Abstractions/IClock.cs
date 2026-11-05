using System;

namespace DhcpServer.Core.Abstractions
{
    /// <summary>Source of the current time; all lease times are UTC instants from here.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }
}
