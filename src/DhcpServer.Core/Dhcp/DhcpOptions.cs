namespace DhcpServer.Core.Dhcp
{
    public enum DhcpMessageType : byte
    {
        Discover = 1,
        Offer = 2,
        Request = 3,
        Decline = 4,
        Ack = 5,
        Nak = 6,
        Release = 7,
        Inform = 8
    }

    /// <summary>Option codes used by this server (RFC 2132).</summary>
    public static class DhcpOptionCode
    {
        public const byte Pad = 0;
        public const byte SubnetMask = 1;
        public const byte Router = 3;
        public const byte HostName = 12;
        public const byte BroadcastAddress = 28;
        public const byte RequestedAddress = 50;
        public const byte LeaseTime = 51;
        public const byte MessageType = 53;
        public const byte ServerIdentifier = 54;
        public const byte ParameterRequestList = 55;
        public const byte RenewalTime = 58;
        public const byte RebindingTime = 59;
        public const byte ClientIdentifier = 61;
        public const byte End = 255;
    }
}
