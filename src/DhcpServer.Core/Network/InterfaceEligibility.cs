using System.Collections.Generic;
using DhcpServer.Core.Runtime;

namespace DhcpServer.Core.Network
{
    public sealed class Eligibility
    {
        private Eligibility(FailureReason reason)
        {
            Reason = reason;
        }

        public bool IsEligible => Reason == null;

        /// <summary>Why the interface is unusable; null when eligible.</summary>
        public FailureReason Reason { get; }

        public static Eligibility Eligible => new Eligibility(null);

        public static Eligibility Unavailable(FailureKind kind) => new Eligibility(new FailureReason(kind));
    }

    /// <summary>Decides whether an interface may be used for serving (Principle I).</summary>
    public static class InterfaceEligibility
    {
        public const int MaxPrefixLength = 30;
        public const int MinPrefixLength = 8;

        /// <summary>
        /// True when the adapter may be given a fixed address by this program: Ethernet or Wi-Fi, in any
        /// status and with any (or no) address. This only decides whether it can be *chosen*; serving is
        /// still gated by <see cref="Evaluate"/>.
        /// </summary>
        public static bool IsConfigurable(InterfaceInfo candidate)
        {
            return candidate.Kind == InterfaceKind.Ethernet || candidate.Kind == InterfaceKind.Wireless;
        }

        public static Eligibility Evaluate(InterfaceInfo candidate, IEnumerable<InterfaceInfo> all)
        {
            if (!candidate.IsUp)
            {
                return Eligibility.Unavailable(FailureKind.Down);
            }

            if (candidate.Kind != InterfaceKind.Ethernet && candidate.Kind != InterfaceKind.Wireless)
            {
                return Eligibility.Unavailable(FailureKind.NotSupportedType);
            }

            if (candidate.Address == null || IpMath.IsLoopback(candidate.Address))
            {
                return Eligibility.Unavailable(FailureKind.NoIpv4Address);
            }

            if (IpMath.IsLinkLocal(candidate.Address))
            {
                return Eligibility.Unavailable(FailureKind.LinkLocalAddress);
            }

            if (candidate.PrefixLength > MaxPrefixLength || candidate.PrefixLength < MinPrefixLength)
            {
                return Eligibility.Unavailable(FailureKind.SubnetTooSmall);
            }

            foreach (var other in all)
            {
                if (other.Id == candidate.Id || !other.IsUp || other.Kind == InterfaceKind.Loopback
                    || other.Address == null || IpMath.IsLoopback(other.Address))
                {
                    continue;
                }

                if (IpMath.Overlaps(candidate.Address, candidate.PrefixLength, other.Address, other.PrefixLength))
                {
                    return Eligibility.Unavailable(FailureKind.SubnetOverlap);
                }
            }

            return Eligibility.Eligible;
        }
    }
}
