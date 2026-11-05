using System;
using System.Collections.Generic;
using System.Net;

namespace DhcpServer.Core.Dhcp
{
    /// <summary>A DHCPv4 / BOOTP message (RFC 2131): fixed header plus options.</summary>
    public sealed class DhcpMessage
    {
        public const int HeaderLength = 236;
        public const int MinimumLength = 240;
        private const int MinimumWireLength = 300;
        private static readonly byte[] MagicCookie = { 99, 130, 83, 99 };

        public byte Op { get; set; }

        public byte HType { get; set; } = 1;

        public byte HLen { get; set; } = 6;

        public byte Hops { get; set; }

        public uint Xid { get; set; }

        public ushort Secs { get; set; }

        public ushort Flags { get; set; }

        public IPAddress CiAddr { get; set; } = IPAddress.Any;

        public IPAddress YiAddr { get; set; } = IPAddress.Any;

        public IPAddress SiAddr { get; set; } = IPAddress.Any;

        public IPAddress GiAddr { get; set; } = IPAddress.Any;

        /// <summary>Client hardware address (6 bytes).</summary>
        public byte[] ClientMac { get; set; } = new byte[6];

        public SortedDictionary<byte, byte[]> Options { get; } = new SortedDictionary<byte, byte[]>();

        public DhcpMessageType? MessageType
        {
            get
            {
                byte[] value;
                return Options.TryGetValue(DhcpOptionCode.MessageType, out value) && value.Length == 1
                    ? (DhcpMessageType?)value[0]
                    : null;
            }
        }

        public bool HasOption(byte code)
        {
            return Options.ContainsKey(code);
        }

        public IPAddress GetAddressOption(byte code)
        {
            byte[] value;
            return Options.TryGetValue(code, out value) && value.Length >= 4
                ? new IPAddress(new[] { value[0], value[1], value[2], value[3] })
                : null;
        }

        public void SetAddressOption(byte code, IPAddress address)
        {
            Options[code] = address.GetAddressBytes();
        }

        public void SetUInt32Option(byte code, uint value)
        {
            Options[code] = new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        public void SetByteOption(byte code, byte value)
        {
            Options[code] = new[] { value };
        }

        /// <summary>Parses a datagram; returns false for anything that is not a well-formed Ethernet DHCP message.</summary>
        public static bool TryParse(byte[] data, out DhcpMessage message)
        {
            message = null;
            if (data == null || data.Length < MinimumLength)
            {
                return false;
            }

            for (var i = 0; i < MagicCookie.Length; i++)
            {
                if (data[HeaderLength + i] != MagicCookie[i])
                {
                    return false;
                }
            }

            if (data[1] != 1 || data[2] != 6)
            {
                return false;
            }

            var m = new DhcpMessage
            {
                Op = data[0],
                HType = data[1],
                HLen = data[2],
                Hops = data[3],
                Xid = ReadUInt32(data, 4),
                Secs = (ushort)((data[8] << 8) | data[9]),
                Flags = (ushort)((data[10] << 8) | data[11]),
                CiAddr = ReadAddress(data, 12),
                YiAddr = ReadAddress(data, 16),
                SiAddr = ReadAddress(data, 20),
                GiAddr = ReadAddress(data, 24)
            };
            Array.Copy(data, 28, m.ClientMac, 0, 6);

            var index = MinimumLength;
            while (index < data.Length)
            {
                var code = data[index++];
                if (code == DhcpOptionCode.Pad)
                {
                    continue;
                }

                if (code == DhcpOptionCode.End)
                {
                    break;
                }

                if (index >= data.Length)
                {
                    return false;
                }

                int length = data[index++];
                if (index + length > data.Length)
                {
                    return false;
                }

                var value = new byte[length];
                Array.Copy(data, index, value, 0, length);
                m.Options[code] = value;
                index += length;
            }

            if (!m.HasOption(DhcpOptionCode.MessageType) || m.Options[DhcpOptionCode.MessageType].Length != 1)
            {
                return false;
            }

            message = m;
            return true;
        }

        public byte[] ToBytes()
        {
            var size = MinimumLength + 1;
            foreach (var option in Options)
            {
                size += 2 + option.Value.Length;
            }

            var data = new byte[Math.Max(size, MinimumWireLength)];
            data[0] = Op;
            data[1] = HType;
            data[2] = HLen;
            data[3] = Hops;
            WriteUInt32(data, 4, Xid);
            data[8] = (byte)(Secs >> 8);
            data[9] = (byte)Secs;
            data[10] = (byte)(Flags >> 8);
            data[11] = (byte)Flags;
            WriteAddress(data, 12, CiAddr);
            WriteAddress(data, 16, YiAddr);
            WriteAddress(data, 20, SiAddr);
            WriteAddress(data, 24, GiAddr);
            Array.Copy(ClientMac, 0, data, 28, Math.Min(6, ClientMac.Length));
            Array.Copy(MagicCookie, 0, data, HeaderLength, MagicCookie.Length);

            var index = MinimumLength;
            foreach (var option in Options)
            {
                data[index++] = option.Key;
                data[index++] = (byte)option.Value.Length;
                Array.Copy(option.Value, 0, data, index, option.Value.Length);
                index += option.Value.Length;
            }

            data[index] = DhcpOptionCode.End;
            return data;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private static IPAddress ReadAddress(byte[] data, int offset)
        {
            return new IPAddress(new[] { data[offset], data[offset + 1], data[offset + 2], data[offset + 3] });
        }

        private static void WriteAddress(byte[] data, int offset, IPAddress address)
        {
            var bytes = (address ?? IPAddress.Any).GetAddressBytes();
            Array.Copy(bytes, 0, data, offset, 4);
        }
    }
}
