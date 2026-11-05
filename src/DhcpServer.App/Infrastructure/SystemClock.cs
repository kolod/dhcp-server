using System;
using DhcpServer.Core.Abstractions;

namespace DhcpServer.App.Infrastructure
{
    internal sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
