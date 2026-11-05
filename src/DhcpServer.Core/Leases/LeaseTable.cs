using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DhcpServer.Core.Network;

namespace DhcpServer.Core.Leases
{
    /// <summary>
    /// Owns the leases, the short-lived offers and the addresses known to be used by someone else.
    /// Thread-safe; all methods take the current time so tests control the clock.
    /// </summary>
    public sealed class LeaseTable
    {
        public const int LeaseSeconds = 14400;
        public const int RenewalSeconds = 7200;
        public const int RebindingSeconds = 12600;
        public const int OfferHoldSeconds = 60;
        public const int ForeignHoldMinutes = 10;
        public const int MaxLeases = 1024;

        private readonly object _sync = new object();
        private readonly Dictionary<string, Lease> _leases = new Dictionary<string, Lease>();
        private readonly Dictionary<string, PendingOffer> _offers = new Dictionary<string, PendingOffer>();
        private readonly Dictionary<uint, DateTime> _foreign = new Dictionary<uint, DateTime>();

        /// <summary>Raised after the visible set of leases changed (never for offers).</summary>
        public event EventHandler Changed;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _leases.Count;
                }
            }
        }

        /// <summary>Replaces the table content (used for leases loaded from disk).</summary>
        public void Load(IEnumerable<Lease> leases)
        {
            lock (_sync)
            {
                _leases.Clear();
                _offers.Clear();
                foreach (var lease in (leases ?? Enumerable.Empty<Lease>()).OrderBy(l => l.Expires))
                {
                    var sameAddress = _leases.Values.FirstOrDefault(l => l.Address.Equals(lease.Address));
                    if (sameAddress != null)
                    {
                        _leases.Remove(sameAddress.Mac);
                    }

                    _leases[lease.Mac] = lease;
                }
            }

            OnChanged();
        }

        public Lease Get(string mac)
        {
            lock (_sync)
            {
                Lease lease;
                return _leases.TryGetValue(mac, out lease) ? lease : null;
            }
        }

        /// <summary>All leases ordered by IP address (numerically).</summary>
        public IReadOnlyList<Lease> Snapshot()
        {
            lock (_sync)
            {
                return _leases.Values.OrderBy(l => IpMath.ToUInt32(l.Address)).ToList();
            }
        }

        /// <summary>
        /// Picks an address for a DISCOVER and reserves it for <see cref="OfferHoldSeconds"/>.
        /// Returns null when the pool is exhausted.
        /// </summary>
        public IPAddress Offer(string mac, IPAddress requested, AddressPool pool, DateTime now, Func<IPAddress, bool> isInUse)
        {
            List<IPAddress> candidates;
            Lease own;
            lock (_sync)
            {
                PurgeOffers(now);
                own = OwnLeaseInPool(mac, pool);
                candidates = BuildCandidates(mac, own, requested, pool);
            }

            foreach (var candidate in candidates)
            {
                var ownActive = own != null && own.Address.Equals(candidate) && own.IsActive(now);

                lock (_sync)
                {
                    if (!IsFree(candidate, mac, pool, now))
                    {
                        continue;
                    }
                }

                // Probing may take hundreds of milliseconds: never hold the lock meanwhile.
                if (!ownActive && isInUse != null && isInUse(candidate))
                {
                    lock (_sync)
                    {
                        _foreign[IpMath.ToUInt32(candidate)] = now.AddMinutes(ForeignHoldMinutes);
                    }

                    continue;
                }

                lock (_sync)
                {
                    if (!IsFree(candidate, mac, pool, now))
                    {
                        continue;
                    }

                    _offers[mac] = new PendingOffer(IpMath.ToUInt32(candidate), now.AddSeconds(OfferHoldSeconds));
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Turns an offer / request into an active lease; null when the address cannot be given to this device.</summary>
        public Lease Confirm(string mac, IPAddress address, string hostName, AddressPool pool, DateTime now)
        {
            Lease lease;
            lock (_sync)
            {
                PurgeOffers(now);
                if (!pool.Contains(address) || !IsFree(address, mac, pool, now))
                {
                    return null;
                }

                Lease previous;
                var host = hostName;
                if (string.IsNullOrEmpty(host) && _leases.TryGetValue(mac, out previous))
                {
                    host = previous.HostName;
                }

                var holder = _leases.Values.FirstOrDefault(l => l.Mac != mac && l.Address.Equals(address));
                if (holder != null)
                {
                    _leases.Remove(holder.Mac);
                }

                lease = new Lease(mac, address, host, now, now.AddSeconds(LeaseSeconds), false, pool.PrefixLength);
                _leases[mac] = lease;
                _offers.Remove(mac);
                TrimLocked(now);
            }

            OnChanged();
            return lease;
        }

        public void Release(string mac, IPAddress address, DateTime now)
        {
            lock (_sync)
            {
                Lease lease;
                if (!_leases.TryGetValue(mac, out lease) || !lease.Address.Equals(address) || lease.IsReleased)
                {
                    return;
                }

                _leases[mac] = lease.AsReleased();
                _offers.Remove(mac);
            }

            OnChanged();
        }

        /// <summary>The client found the address in use: forget our assignment and avoid the address for a while.</summary>
        public void Decline(string mac, IPAddress address, DateTime now)
        {
            var changed = false;
            lock (_sync)
            {
                Lease lease;
                if (_leases.TryGetValue(mac, out lease) && lease.Address.Equals(address))
                {
                    _leases.Remove(mac);
                    changed = true;
                }

                _offers.Remove(mac);
                _foreign[IpMath.ToUInt32(address)] = now.AddMinutes(ForeignHoldMinutes);
            }

            if (changed)
            {
                OnChanged();
            }
        }

        public void CancelOffer(string mac)
        {
            lock (_sync)
            {
                _offers.Remove(mac);
            }
        }

        private Lease OwnLeaseInPool(string mac, AddressPool pool)
        {
            Lease lease;
            if (_leases.TryGetValue(mac, out lease) && pool.IsSameSubnet(lease.Network, lease.PrefixLength) && pool.Contains(lease.Address))
            {
                return lease;
            }

            return null;
        }

        private List<IPAddress> BuildCandidates(string mac, Lease own, IPAddress requested, AddressPool pool)
        {
            var result = new List<IPAddress>();
            var seen = new HashSet<uint>();
            Action<IPAddress> add = a =>
            {
                if (seen.Add(IpMath.ToUInt32(a)))
                {
                    result.Add(a);
                }
            };

            if (own != null)
            {
                add(own.Address);
            }

            if (requested != null && pool.Contains(requested))
            {
                add(requested);
            }

            var used = new Dictionary<uint, Lease>();
            foreach (var lease in _leases.Values)
            {
                if (lease.Mac != mac && pool.IsSameSubnet(lease.Network, lease.PrefixLength))
                {
                    used[IpMath.ToUInt32(lease.Address)] = lease;
                }
            }

            foreach (var address in pool.Addresses)
            {
                if (!used.ContainsKey(IpMath.ToUInt32(address)))
                {
                    add(address);
                }
            }

            foreach (var lease in used.Values.OrderBy(l => l.Expires))
            {
                add(lease.Address);
            }

            return result;
        }

        private bool IsFree(IPAddress address, string mac, AddressPool pool, DateTime now)
        {
            var value = IpMath.ToUInt32(address);

            DateTime until;
            if (_foreign.TryGetValue(value, out until))
            {
                if (until > now)
                {
                    return false;
                }

                _foreign.Remove(value);
            }

            foreach (var lease in _leases.Values)
            {
                if (lease.Mac != mac && lease.Address.Equals(address) && lease.IsActive(now)
                    && pool.IsSameSubnet(lease.Network, lease.PrefixLength))
                {
                    return false;
                }
            }

            foreach (var offer in _offers)
            {
                if (offer.Key != mac && offer.Value.Address == value && offer.Value.Expires > now)
                {
                    return false;
                }
            }

            return true;
        }

        private void PurgeOffers(DateTime now)
        {
            var expired = _offers.Where(o => o.Value.Expires <= now).Select(o => o.Key).ToList();
            foreach (var key in expired)
            {
                _offers.Remove(key);
            }
        }

        private void TrimLocked(DateTime now)
        {
            while (_leases.Count > MaxLeases)
            {
                var victim = _leases.Values
                    .OrderBy(l => l.IsActive(now) ? 1 : 0)
                    .ThenBy(l => l.Expires)
                    .First();
                _leases.Remove(victim.Mac);
            }
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private struct PendingOffer
        {
            public PendingOffer(uint address, DateTime expires)
            {
                Address = address;
                Expires = expires;
            }

            public uint Address { get; }

            public DateTime Expires { get; }
        }
    }
}
