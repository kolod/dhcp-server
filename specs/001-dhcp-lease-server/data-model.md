# Data Model: DHCP Lease Server

All types live in `DhcpServer.Core` (no WPF reference) unless marked *(App)*. Field types are
conceptual; C# 7.3 immutable classes/structs are used.

## InterfaceInfo

Snapshot of one network adapter, produced by `IInterfaceProvider` (real implementation *(App)*,
fakes in tests).

| Field | Meaning |
|-------|---------|
| `Id` | Stable adapter id (GUID string); used to remember the selection |
| `Name` | Friendly name shown to the user |
| `Kind` | Ethernet, Wireless, Loopback, Tunnel, Other |
| `IsUp` | Operational status is Up |
| `Address` | Primary unicast IPv4 address, or none |
| `PrefixLength` | Subnet mask length |
| `Gateway` | Default gateway of this adapter if any |
| `MacAddress` | Adapter hardware address (used as probe source seed only) |

Derived (via `InterfaceEligibility.Evaluate(interface, allInterfaces)`) → `Eligibility`:
`Eligible` or `Unavailable(reason)` where reason ∈ `Down`, `NotSupportedType`, `NoIpv4Address`,
`LinkLocalAddress`, `SubnetTooSmall`, `SubnetOverlap`. Rules and order: research D5.

## AddressPool

Immutable; built from `(serverAddress, prefixLength, gateway)`.

- `Network`, `Mask`, `Broadcast` — subnet identity.
- `Server` — excluded from leasing; `Gateway` — excluded if inside the subnet.
- `Addresses` — ordered usable window (≤ 254, research D6).
- `Contains(ip)` — true only for addresses in `Addresses`.

Invariants: never contains `Network`, `Broadcast`, `Server`, `Gateway` (Principle I, tested).

## Lease

| Field | Meaning |
|-------|---------|
| `Mac` | Client hardware address (6 bytes) — the identity key |
| `Address` | Assigned IPv4 address |
| `HostName` | Option 12 value if reported, else empty |
| `ClientId` | Option 61 raw bytes if reported (informational, not an identity) |
| `Start` | UTC instant of the last ACK that created or renewed the lease |
| `Expires` | UTC instant when it lapses |
| `State` | `Active`, `Expired`, `Released` (computed for display from `Expires`/release flag and the clock) |
| `Subnet` | Network + prefix at issue time (to separate leases of other subnets) |

Relationships: one lease per `Mac`; one lease per `Address` at a time (enforced by `LeaseTable`).

State transitions:

```text
(none) --ACK--> Active --renew ACK--> Active
Active --clock >= Expires--> Expired
Active --RELEASE--> Released
Expired/Released --ACK for same MAC--> Active   (same address preferred)
Expired/Released --address reused by another MAC (pool pressure)--> entry replaced
```

Server session end does not change states by itself; entries simply age with the clock and the list
shows `Inactive` for anything not `Active` or whenever the session is not running (FR-013, US2-5).

## Offer (in memory only)

`Mac`, `Address`, `ExpiresAt` (now + 60 s). Reserves an address between OFFER and REQUEST; not shown
in the list, not persisted.

## LeaseTable

Owns leases and offers; all methods take `now` from `IClock`.

- `TryAllocate(mac, requestedIp, pool, isInUse)` → address or `PoolExhausted` (D6 offer order; `isInUse` is the ARP/ICMP prober, bypassed for the device's own valid lease).
- `Confirm(mac, address, hostName, clientId, now)` → `Lease` (ACK) or reject (→ NAK).
- `Renew`, `Release(mac, address)`.
- `Snapshot(now)` → ordered immutable list for the UI.
- Raises `Changed` after each mutation.

## ServerState / ServerSession

States: `Stopped`, `Starting`, `Running`, `Failed`.

```text
Stopped --Start--> Starting --checks pass + socket bound--> Running
Starting --check fails / bind fails--> Failed(reason) --Start--> Starting
Running --Stop | interface lost | exit--> Stopped   (interface lost also carries a reason)
```

`ServerSession` holds: chosen `InterfaceInfo`, `AddressPool`, state, last `FailureReason`
(a resource key + arguments, never user-visible text), the receive endpoint. It exposes
`StateChanged` and delegates packets to the engine.

## FailureReason (resource keys, see ui-contract.md)

`NoInterfaceChosen`, `InterfaceDown`, `InterfaceUnsupported`, `NoIpv4Address`, `LinkLocalAddress`,
`SubnetTooSmall`, `SubnetOverlap`, `OtherDhcpServer(address)`, `ProbeInconclusive`,
`PortInUse`, `AccessDenied`, `InterfaceLost`, `UnexpectedError(message)`.

## DhcpMessage (wire model)

Fixed BOOTP header (`op`, `htype`, `hlen`, `hops`, `xid`, `secs`, `flags`, `ciaddr`, `yiaddr`,
`siaddr`, `giaddr`, `chaddr`) plus the options used: 1, 3, 12, 28, 50, 51, 53, 54, 55, 58, 59, 61,
255. Parser rejects: length < 240, wrong magic cookie, `op != 1` on input, `hlen != 6`/`htype != 1`
(non-Ethernet clients are ignored), missing option 53. Behavior table: `contracts/dhcp-behavior.md`.

## Abstractions (Principle VI)

`IClock`, `IInterfaceProvider`, `IDhcpEndpoint` (send/receive/close), `IEndpointFactory`,
`IAddressProber`, `IDhcpServerProbe`, `ILeaseStore`, `ISettingsStore` — real implementations *(App)*,
fakes in `tests/DhcpServer.Tests`.
