using System;
using System.Net;
using DhcpServer.Core.Leases;

namespace DhcpServer.Core.Dhcp
{
    /// <summary>
    /// The protocol brain: turns one received datagram into at most one reply datagram
    /// (see specs/001-dhcp-lease-server/contracts/dhcp-behavior.md). No I/O.
    /// </summary>
    public sealed class DhcpEngine
    {
        private readonly AddressPool _pool;
        private readonly LeaseTable _table;
        private readonly Func<IPAddress, bool> _isInUse;

        public DhcpEngine(AddressPool pool, LeaseTable table, Func<IPAddress, bool> isInUse = null)
        {
            _pool = pool;
            _table = table;
            _isInUse = isInUse;
        }

        /// <summary>Raised when a device asked for an address and none was free.</summary>
        public event EventHandler PoolExhausted;

        /// <summary>Returns the reply to broadcast, or null when the datagram needs no answer.</summary>
        public byte[] Handle(byte[] datagram, DateTime now)
        {
            DhcpMessage request;
            if (!DhcpMessage.TryParse(datagram, out request) || request.Op != 1 || !request.GiAddr.Equals(IPAddress.Any))
            {
                return null;
            }

            var mac = MacAddress.ToKey(request.ClientMac);
            var reply = Dispatch(request, mac, now);
            return reply?.ToBytes();
        }

        private DhcpMessage Dispatch(DhcpMessage request, string mac, DateTime now)
        {
            switch (request.MessageType)
            {
                case DhcpMessageType.Discover:
                    return OnDiscover(request, mac, now);
                case DhcpMessageType.Request:
                    return OnRequest(request, mac, now);
                case DhcpMessageType.Decline:
                    OnDecline(request, mac, now);
                    return null;
                case DhcpMessageType.Release:
                    _table.Release(mac, request.CiAddr, now);
                    return null;
                case DhcpMessageType.Inform:
                    return OnInform(request);
                default:
                    return null;
            }
        }

        private DhcpMessage OnDiscover(DhcpMessage request, string mac, DateTime now)
        {
            var address = _table.Offer(mac, request.GetAddressOption(DhcpOptionCode.RequestedAddress), _pool, now, _isInUse);
            if (address == null)
            {
                PoolExhausted?.Invoke(this, EventArgs.Empty);
                return null;
            }

            var offer = NewReply(request, DhcpMessageType.Offer);
            offer.YiAddr = address;
            AddLeaseOptions(offer);
            AddNetworkOptions(offer);
            return offer;
        }

        private DhcpMessage OnRequest(DhcpMessage request, string mac, DateTime now)
        {
            var serverId = request.GetAddressOption(DhcpOptionCode.ServerIdentifier);
            if (serverId != null && !serverId.Equals(_pool.Server))
            {
                // The client chose another server: drop our pending offer, stay silent.
                _table.CancelOffer(mac);
                return null;
            }

            var requested = request.GetAddressOption(DhcpOptionCode.RequestedAddress);
            if (requested == null && !request.CiAddr.Equals(IPAddress.Any))
            {
                requested = request.CiAddr;
            }

            if (requested == null)
            {
                return null;
            }

            byte[] hostName;
            request.Options.TryGetValue(DhcpOptionCode.HostName, out hostName);
            var name = hostName == null ? null : System.Text.Encoding.ASCII.GetString(hostName);

            var lease = _table.Confirm(mac, requested, name, _pool, now);
            if (lease == null)
            {
                return NewReply(request, DhcpMessageType.Nak);
            }

            var ack = NewReply(request, DhcpMessageType.Ack);
            ack.YiAddr = lease.Address;
            AddLeaseOptions(ack);
            AddNetworkOptions(ack);
            return ack;
        }

        private void OnDecline(DhcpMessage request, string mac, DateTime now)
        {
            var serverId = request.GetAddressOption(DhcpOptionCode.ServerIdentifier);
            var address = request.GetAddressOption(DhcpOptionCode.RequestedAddress);
            if (address == null || (serverId != null && !serverId.Equals(_pool.Server)) || !_pool.Contains(address))
            {
                return;
            }

            _table.Decline(mac, address, now);
        }

        private DhcpMessage OnInform(DhcpMessage request)
        {
            var ack = NewReply(request, DhcpMessageType.Ack);
            AddNetworkOptions(ack);
            return ack;
        }

        private DhcpMessage NewReply(DhcpMessage request, DhcpMessageType type)
        {
            var reply = new DhcpMessage
            {
                Op = 2,
                Xid = request.Xid,
                Flags = request.Flags,
                ClientMac = request.ClientMac
            };
            reply.SetByteOption(DhcpOptionCode.MessageType, (byte)type);
            reply.SetAddressOption(DhcpOptionCode.ServerIdentifier, _pool.Server);
            return reply;
        }

        private static void AddLeaseOptions(DhcpMessage reply)
        {
            reply.SetUInt32Option(DhcpOptionCode.LeaseTime, LeaseTable.LeaseSeconds);
            reply.SetUInt32Option(DhcpOptionCode.RenewalTime, LeaseTable.RenewalSeconds);
            reply.SetUInt32Option(DhcpOptionCode.RebindingTime, LeaseTable.RebindingSeconds);
        }

        private void AddNetworkOptions(DhcpMessage reply)
        {
            reply.SetAddressOption(DhcpOptionCode.SubnetMask, _pool.Mask);
            reply.SetAddressOption(DhcpOptionCode.Router, _pool.Server);
            reply.SetAddressOption(DhcpOptionCode.BroadcastAddress, _pool.Broadcast);
        }
    }
}
