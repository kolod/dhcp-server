using System.Linq;
using System.Threading;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Runtime
{
    /// <summary>The ordered pre-start checks of Principle I. Binding the socket is the caller's last step.</summary>
    public static class StartGate
    {
        public sealed class Result
        {
            public InterfaceInfo Interface { get; set; }

            /// <summary>Null when all checks passed.</summary>
            public FailureReason Failure { get; set; }

            public bool Cancelled { get; set; }
        }

        public static Result Check(
            InterfaceInfo chosen,
            IInterfaceProvider interfaces,
            IDhcpServerProbe probe,
            CancellationToken cancellation)
        {
            if (chosen == null)
            {
                return Fail(null, FailureKind.NoInterfaceChosen);
            }

            var all = interfaces.GetInterfaces();
            var fresh = all.FirstOrDefault(i => i.Id == chosen.Id);
            if (fresh == null)
            {
                return Fail(null, FailureKind.Down);
            }

            var eligibility = InterfaceEligibility.Evaluate(fresh, all);
            if (!eligibility.IsEligible)
            {
                return new Result { Interface = fresh, Failure = eligibility.Reason };
            }

            var outcome = probe.Probe(fresh.Address, cancellation);
            if (cancellation.IsCancellationRequested)
            {
                return new Result { Interface = fresh, Cancelled = true };
            }

            switch (outcome.Outcome)
            {
                case ProbeOutcome.ServerFound:
                    return new Result
                    {
                        Interface = fresh,
                        Failure = new FailureReason(FailureKind.OtherDhcpServer, outcome.Server?.ToString())
                    };
                case ProbeOutcome.Inconclusive:
                    return Fail(fresh, FailureKind.ProbeInconclusive);
                default:
                    return new Result { Interface = fresh };
            }
        }

        private static Result Fail(InterfaceInfo info, FailureKind kind)
        {
            return new Result { Interface = info, Failure = new FailureReason(kind) };
        }
    }
}
