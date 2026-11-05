using System.Net;
using System.Threading;

namespace DhcpServer.Core.Abstractions
{
    public enum ProbeOutcome
    {
        NoServer,
        ServerFound,
        Inconclusive
    }

    public sealed class ProbeResult
    {
        private ProbeResult(ProbeOutcome outcome, IPAddress server)
        {
            Outcome = outcome;
            Server = server;
        }

        public ProbeOutcome Outcome { get; }

        public IPAddress Server { get; }

        public static ProbeResult NoServer => new ProbeResult(ProbeOutcome.NoServer, null);

        public static ProbeResult Inconclusive => new ProbeResult(ProbeOutcome.Inconclusive, null);

        public static ProbeResult ServerFound(IPAddress server) => new ProbeResult(ProbeOutcome.ServerFound, server);
    }

    /// <summary>Looks for an existing DHCP server on the chosen interface before we start serving.</summary>
    public interface IDhcpServerProbe
    {
        ProbeResult Probe(IPAddress viaInterface, CancellationToken cancellation);
    }
}
