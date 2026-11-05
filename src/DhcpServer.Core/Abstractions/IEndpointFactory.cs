using System;
using System.Net;
using DhcpServer.Core.Runtime;

namespace DhcpServer.Core.Abstractions
{
    public interface IEndpointFactory
    {
        /// <summary>Binds the server endpoint to <paramref name="localAddress"/>; throws <see cref="EndpointBindException"/>.</summary>
        IDhcpEndpoint Bind(IPAddress localAddress);
    }

    public sealed class EndpointBindException : Exception
    {
        public EndpointBindException(FailureReason reason, Exception inner = null)
            : base(reason == null ? "Bind failed" : reason.ResourceKey, inner)
        {
            Reason = reason;
        }

        public FailureReason Reason { get; }
    }
}
