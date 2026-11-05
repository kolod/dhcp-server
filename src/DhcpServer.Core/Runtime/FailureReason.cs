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
        Unexpected,
        AddressApplyFailed,
        AddressRestoreFailed,
        SettingsLocked,
        RecordNotWritable,
        AddressNotReady
    }

    /// <summary>Why the server refused to start or stopped; maps to a localized string key.</summary>
    public sealed class FailureReason
    {
        public FailureReason(FailureKind kind, string argument = null, Addressing.AdapterConfig previous = null)
        {
            Kind = kind;
            Argument = argument;
            Previous = previous;
        }

        /// <summary>For a failed restore: the configuration that should be on the adapter (shown so the user can set it by hand).</summary>
        public Addressing.AdapterConfig Previous { get; }

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
