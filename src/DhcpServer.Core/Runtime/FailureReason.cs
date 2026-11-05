namespace DhcpServer.Core.Runtime
{
    public enum FailureKind
    {
        NoInterfaceChosen,
        Down,
        NotSupportedType,
        NoIpv4Address,
        LinkLocalAddress,
        SubnetTooSmall,
        SubnetOverlap,
        OtherDhcpServer,
        ProbeInconclusive,
        PortInUse,
        AccessDenied,
        InterfaceLost,
        Unexpected
    }

    /// <summary>Why the server refused to start or stopped; maps to a localized string key.</summary>
    public sealed class FailureReason
    {
        public FailureReason(FailureKind kind, string argument = null)
        {
            Kind = kind;
            Argument = argument;
        }

        public FailureKind Kind { get; }

        /// <summary>Optional value for the {0} placeholder (an address, an error message).</summary>
        public string Argument { get; }

        public string ResourceKey => "Reason_" + Kind;

        public override string ToString()
        {
            return ResourceKey + (Argument == null ? string.Empty : " (" + Argument + ")");
        }
    }
}
