# Contract: DHCP server behavior (wire level)

The server speaks DHCPv4 per RFC 2131/2132 over UDP. Only what a client can observe is specified.

## Transport

| Aspect | Value |
|--------|-------|
| Listen | `<chosen interface IPv4>:67` only (never `0.0.0.0`) |
| Reply | `255.255.255.255:68`, source = chosen interface IPv4 |
| Ignored input | `giaddr != 0`, `op != BOOTREQUEST`, `htype != 1` or `hlen != 6`, bad magic cookie, no option 53, datagram < 240 bytes, unknown message types |

## Message handling

| Client message | Condition | Server action |
|----------------|-----------|---------------|
| DISCOVER | free address available | OFFER (`yiaddr` per offer order, lease 4 h, options below) |
| DISCOVER | pool exhausted | no reply; session raises `PoolExhausted` notice (FR-016) |
| REQUEST (selecting) | server id = ours, address matches the offer/lease | ACK, lease becomes Active |
| REQUEST (selecting) | server id ≠ ours | no reply; release our pending offer for that MAC |
| REQUEST (init-reboot) | requested IP in pool and not leased to another MAC | ACK |
| REQUEST (init-reboot) | IP outside the subnet or leased to another MAC | NAK |
| REQUEST (renew/rebind) | `ciaddr` matches the MAC's lease | ACK with a fresh lease time |
| REQUEST | requested IP is the server or gateway address | NAK |
| DECLINE | address in our pool | mark address "in use by someone else" for 10 min; drop the lease |
| RELEASE | `ciaddr` matches the MAC's lease | lease → Released; no reply |
| INFORM | any | ACK with options only, `yiaddr = 0`, no lease created |
| OFFER / ACK / NAK from others | any | ignored by the server (seen only by the start-up probe) |

## Options in OFFER / ACK

| Option | Value |
|--------|-------|
| 53 | 2 (OFFER) / 5 (ACK) / 6 (NAK) |
| 54 server identifier | chosen interface IPv4 |
| 51 lease time | 14 400 s |
| 58 / 59 | 7 200 s / 12 600 s |
| 1 subnet mask | pool mask |
| 3 router | chosen interface IPv4 |
| 28 broadcast | subnet broadcast |
| 255 | end |

A NAK carries only options 53 and 54; no message text (option 56) is sent.

## Invariants (each has a test)

1. `yiaddr` is always inside the pool; never network, broadcast, server or gateway address.
2. No two MACs hold an Active lease or live offer for the same address.
3. A known MAC gets its previous address when it is free.
4. Nothing is sent on any socket other than the one bound to the chosen interface.
5. Malformed input never throws out of the receive loop.

## Start-up probe (separate, before the server socket is bound)

DISCOVER from `<chosen IPv4>:68`, random locally-administered MAC (`02:xx:xx:xx:xx:xx`), no options
requesting a lease beyond 53/55; wait 3 s for an OFFER, repeat once. An OFFER from any source ⇒ start
refused (`OtherDhcpServer`). Bind failure ⇒ `ProbeInconclusive`, start refused (research D3).
