# Research: DHCP Lease Server

All Technical Context items were resolvable from the constitution and the existing scaffold; the
decisions below record the non-obvious choices. Items marked **Validate** must be confirmed by a
spike task on real Windows 7 and Windows 11 machines (Principle VI manual checklist).

## D1. Interface isolation: bind to the interface's own IPv4 address

- **Decision**: The server socket is bound to `<interface IPv4>:67` (never `0.0.0.0`), with
  `SO_BROADCAST` enabled and `ExclusiveAddressUse` set. Requests are additionally dropped when
  `giaddr != 0` (no relay support).
- **Rationale**: On Windows a socket bound to a specific local address receives the limited
  broadcasts (`255.255.255.255`) that arrive on that interface and nothing from other interfaces,
  which gives FR-002 / SC-005 with no extra API. Replies are sent from the same socket, so the
  source address is the chosen interface.
- **Alternatives**: bind `0.0.0.0` and filter with `IP_PKTINFO` through `WSARecvMsg` (more P/Invoke,
  more code, same result); raw sockets / packet capture (needs a driver or dependency, forbidden by
  Principles II/III).
- **Validate**: with two interfaces on different subnets, a DISCOVER sent on the second interface is
  never seen by the engine (automated with a fake endpoint; manual with real hardware).

## D2. Replies are broadcast

- **Decision**: Every OFFER/ACK/NAK is sent to `255.255.255.255:68` from the bound socket,
  regardless of the client's `broadcast` flag.
- **Rationale**: A client without an IP address cannot always receive a unicast frame addressed to
  its future IP, and unicast delivery would need a static ARP entry (extra system change, Principle
  II). On a point-to-point link the cost of broadcast is nil. Renewals (unicast REQUEST from a client
  that already has the address) are also answered by broadcast, which every client accepts.
- **Alternatives**: unicast to `yiaddr` via `SendARP`-seeded neighbor entry (system change, rejected).

## D3. Detecting an existing DHCP server (Principle I)

- **Decision**: Before binding port 67, send one DHCPDISCOVER with a random locally administered
  MAC from `<interface IPv4>:68` and listen for OFFER replies for 3 seconds, twice (6 s worst case).
  Any OFFER, or any reply from an address other than our own, means "another server present" and the
  start is refused. No lease is ever requested, so there is nothing to release.
- **Rationale**: This is exactly how rogue-DHCP detectors work and needs plain sockets only.
- **Risk / Validate**: the Windows DHCP Client service may already hold UDP 68 on some machines. The
  probe binds with `ReuseAddress`; if the bind still fails, the probe is *inconclusive* and the
  start is **refused** with an explanation (safety first), not skipped. If the spike shows this
  blocks normal use, the follow-up is a plan amendment (e.g. temporary request through the OS) —
  not silently skipping the probe.

## D4. Probing whether an address is already in use (FR-006)

- **Decision**: Before OFFERing an address, check it with `SendARP` (iphlpapi, Windows 7 SP1+) bound
  to the chosen interface's source address; if ARP gives no answer, one ICMP echo with a 300 ms
  timeout as a second opinion. An answer marks the address "foreign-in-use" for 10 minutes and the
  next free address is tried.
- **Rationale**: ARP works on the local segment even when hosts block ping; ICMP covers hosts that
  filter ARP replies poorly (rare). Both are available without elevation extras or dependencies.
- **Alternatives**: ICMP only (many IoT devices and firewalls drop it) — rejected.
- **Cost**: probing adds up to ~0.5 s to the first OFFER of a new device; SC-002 (2 s) holds.
  Re-offers to a known device with a valid lease skip probing.

## D5. Interface eligibility (FR-003, Principle I)

- **Decision**: An interface is *eligible* when all hold: operational status Up; type Ethernet or
  Wireless 802.11 (loopback, tunnel, PPP and virtual `Tunnel`/`Ppp` types excluded); has a unicast
  IPv4 address that is not link-local (`169.254/16`) or loopback; mask prefix length ≤ 30; its subnet
  does not overlap the subnet of any *other* Up interface. Otherwise it is listed as unavailable with
  one reason key (first failing rule, in the order above).
- **Rationale**: Matches the constitution word for word; rules are pure functions over a small
  `InterfaceInfo` snapshot, so they are unit-testable without a NIC.
- **Edge**: Virtual Ethernet adapters (Hyper-V, VPN TAP) report type Ethernet and are listed; the
  user chooses consciously. They are still subject to the overlap rule.

## D6. Address pool

- **Decision**: Usable addresses = subnet hosts minus network, broadcast, the server's own address,
  and the interface's configured default gateway if it lies in the subnet. The pool is capped to a
  window of **254 addresses** starting at the first usable address above the lowest one that is not
  excluded; for a /24 this is simply the whole host range. The window is deterministic (no
  randomness) so that tests and users can predict results.
- **Rationale**: Keeps lease list and probing bounded on wide masks (/16, /8) while covering the
  stated scale (a few dozen devices, SC-006 ≥ 30).
- **Offer order**: (1) the device's existing/previous address if valid and free; (2) the address the
  client requested (option 50) if in pool and free; (3) lowest free address.

## D7. Lease timing

- **Decision**: Default lease 4 hours (14 400 s), T1 = 50 %, T2 = 87.5 %. An OFFER holds its address
  reserved for 60 s. Expired/released leases stay in the table (state *Expired*/*Released*) so a
  returning MAC gets its old address back (FR-007); their address becomes "reusable" and is given to
  other devices only after all never-used addresses are exhausted (least-recently-used first).
- **Rationale**: Stable addresses are the main value for IoT debugging. Durations are constants in
  the core for now; configurability is a later feature (spec Assumptions).

## D8. Time source

- **Decision**: All expiry times are UTC instants obtained through `IClock`. Time-zone and DST
  changes therefore cannot affect leases. A manual clock change is not compensated (documented
  limitation; a lease that appears expired is simply re-issued with the same address when the device
  asks again, so no device is harmed).
- **Alternatives**: monotonic-time bookkeeping with periodic re-anchoring — rejected as YAGNI.

## D9. Lease persistence (FR-013)

- **Decision**: A plain UTF-8 text file `DhcpServer.leases` next to the executable, one lease per
  line, tab-separated, with a `#DhcpServer leases v1` header (see `contracts/lease-file.md`).
  Written to a temp file and replaced atomically on every lease change, throttled to at most once
  per second plus a final flush on stop/exit. A failure to read or write is non-fatal (a banner
  hint in the UI, leases live in memory only). Corrupt lines are skipped, not fatal.
- **Rationale**: Constitution Principle III allows files in the application's own folder; settings
  stay in `DhcpServer.ini` (language, last interface) via the existing `IniDocument`, because leases
  are a record list that does not fit the key/value shape.
- **Alternatives**: SQLite/JSON libraries (runtime dependencies, rejected); inside the INI file
  (mixes concerns, rewrites on every renewal).

## D10. Interface loss detection (FR-014)

- **Decision**: Subscribe to `NetworkChange.NetworkAddressChanged` / `NetworkAvailabilityChanged`
  and, as a safety net, re-evaluate the chosen interface every 2 seconds on a timer. Any of:
  disappeared, not Up, address changed, → stop immediately (close the socket first, then update
  state) and tell the user.
- **Rationale**: Events are not delivered reliably for every cable-unplug on every driver; the poll
  guarantees the "stops immediately" promise within 2 s, the event usually makes it near-instant.

## D11. Threading and UI updates

- **Decision**: One dedicated receive thread per session (blocking `ReceiveFrom`, closed by
  disposing the socket to stop). The engine is a synchronous pure function guarded by a lock; the
  session raises `LeasesChanged` / `StateChanged`, and the view model marshals to the UI thread
  through a `SynchronizationContext` captured at construction (testable with a fake context).
- **Rationale**: Simplest correct model for a trickle of packets; no `async` plumbing needed on
  `net48`/C# 7.3, no framework (Principle VII).

## D12. Reply options sent to clients

- **Decision**: subnet mask (1), router (3) = the server's address, broadcast address (28), lease
  time (51), message type (53), server identifier (54), renewal T1 (58), rebinding T2 (59). No DNS
  option (the laptop is not a DNS server; sharing is a separate feature).
- **Rationale**: Gives the "mask and gateway/server information" of User Story 1. Advertising the
  laptop as router is harmless for same-subnet traffic and makes a later Internet-sharing feature
  work with no change.
- **Validate**: confirm with one real IoT device that option 3 pointing to the laptop does not break
  it when Internet sharing is off.

## D13. Windows Firewall

- **Decision**: The application creates no firewall rules (Principle II). On first start Windows may
  show its own "allow access" prompt; the manual checklist covers it and the "failed/no traffic"
  hint in the UI mentions it. Directly connected interfaces are often classified *Public* by
  Windows; allowing the prompt for Public networks is needed in that case.
- **Alternatives**: adding a rule via `netsh advfirewall` — forbidden by Principle II.

## D14. Localization impact

- **Decision**: ~45 new string keys (see `contracts/ui-contract.md`), each added to all six
  `Strings*.resx` files with real translations; `LocalizationTests` already enforces parity.

## Spike results (T002)

| Date | OS | Check | Result |
|------|----|-------|--------|
| 2026-11-05 | Windows 11 Pro (build 26300), DHCP Client service running, not elevated | Bind `<interface IPv4>:68` with `ReuseAddress` on four adapters (Ethernet, two virtual Ethernet, Hyper-V vEthernet) | OK on all — the start-up probe (D3) can bind the client port next to the DHCP Client service |
| 2026-11-05 | same | Bind `<interface IPv4>:67` with `ExclusiveAddressUse` and broadcast enabled | OK on all; binding needs no elevation here (the manifest still demands it, Principle III) |
| open | Windows 7 SP1 | same two bind checks | **Not yet verified** — covered by manual checklist steps 8 and 11 |
| open | any, two real hosts | D1 broadcast isolation, D12 router option on a real IoT device | **Not yet verified** — manual checklist steps 14 and 19 |

## Implementation notes (deviations from the text above)

- **D4**: `SendARP` has no timeout parameter and can block for seconds on an unanswered address. The
  prober therefore runs ARP and the ICMP echo in parallel and gives up after a 400 ms budget
  (`ArpIcmpProber`), treating "no answer within the budget" as "not in use".
- **D6**: the pool window is simply the first 254 usable addresses (ascending) after excluding the
  network, broadcast, server and gateway addresses; there is no "start above the lowest" rule.
