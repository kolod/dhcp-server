# Implementation Plan: DHCP Lease Server

**Branch**: `001-dhcp-lease-server` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-dhcp-lease-server/spec.md`

## Summary

A DHCPv4 server bound to exactly one user-chosen interface that gives addresses to IoT devices and
shows a live lease list. Technical approach (see [research.md](research.md)): plain UDP sockets bound
to the chosen interface's own address (interface isolation), a pure protocol engine in
`DhcpServer.Core` behind small abstractions (clock, sockets, interfaces, probers, lease store), a
start gate that refuses unsafe situations (other DHCP server, down/unsupported/overlapping interface),
ARP/ICMP probing before offering an address, leases persisted to a text file next to the executable,
and a WPF window (interface selector, Start/Stop, lease grid) using the existing MVVM and
localization infrastructure.

## Technical Context

**Language/Version**: C# 7.3

**Primary Dependencies**: none beyond the BCL (`System.Net.Sockets`, `System.Net.NetworkInformation`) and P/Invoke to `iphlpapi!SendARP`

**Storage**: `DhcpServer.ini` (language, last interface; existing `IniDocument`) and `DhcpServer.leases` (tab-separated text, [contracts/lease-file.md](contracts/lease-file.md)), both next to the executable

**Testing**: xUnit on `net48` with fakes for every external resource; Pester not affected; manual checklist per [quickstart.md](quickstart.md)

**Target Platform**: Windows 7 SP1 – 11, x86/x64, .NET Framework 4.8, WPF software-rendering safe

**Project Type**: desktop-app (single portable executable)

**Performance Goals**: first OFFER < 2 s (incl. ≤ 0.5 s address probing); lease list updated < 1 s; start-up probe ≤ 6 s

**Constraints**: single exe < 5 MB, no new DLLs, no registry/service/firewall changes, administrator manifest, no unsolicited start

**Scale/Scope**: one interface, one pool (≤ 254 addresses), ≥ 30 simultaneous leases, ≤ 1 024 remembered leases

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the plan satisfies it |
|-----------|--------|---------------------------|
| I. Network Safety First | PASS | Bind to chosen interface address only (D1); start gate with other-server probe, eligibility rules, overlap check (D3, D5); pool excludes server/gateway; ARP/ICMP check before offering (D4); stop on interface loss within 2 s (D10); no sharing code at all. Each rule has an automated test (quickstart table). |
| II. Reversible System Changes | PASS | The feature changes no PC configuration: no IP, ICS, firewall, service or registry edits. Firewall prompt handled by documentation only (D13). |
| III. Portable & Compact | PASS | No new dependencies; two files beside the exe, failures non-fatal; manifest unchanged (`requireAdministrator`). Size guarded by `check-artifact.ps1`. |
| IV. Windows 7 – 11 | PASS | Only BCL + `SendARP` (Win7 SP1+); plain DataGrid, no hardware acceleration needs. Validate items in research D1/D3/D12 on Win7. |
| V. English-first / Localized | PASS | ~45 new keys in all six `.resx` ([ui-contract.md](contracts/ui-contract.md)); no text on the wire or in code; layout auto-sized. |
| VI. Testable Core | PASS | Engine, pool, lease table, gate, session, lease file in Core behind interfaces; real sockets/NIC/ARP in App; MVVM view models. Manual checklist extended. |
| VII. Simplicity | PASS | One interface/pool, IPv4 only; no DI container or MVVM framework; constants instead of settings UI; out-of-scope items (IP configuration, sharing, probing services) excluded. |

No violations; Complexity Tracking not needed.

**Post-design re-check (after Phase 1)**: PASS. The only open risks are validation items, not
violations: UDP 68 availability for the probe (D3, resolved conservatively — refuse, never skip),
router option behavior on a real device (D12), and the Windows Firewall prompt (D13).

## Project Structure

### Documentation (this feature)

```text
specs/001-dhcp-lease-server/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── dhcp-behavior.md
│   ├── lease-file.md
│   └── ui-contract.md
├── checklists/requirements.md
└── tasks.md             # produced by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DhcpServer.Core/                 # no WPF; compiled into the exe as linked sources
│   ├── Settings/IniDocument.cs      # existing
│   ├── Abstractions/                # IClock, IInterfaceProvider, IDhcpEndpoint, IEndpointFactory,
│   │                                #   IAddressProber, IDhcpServerProbe, ILeaseStore
│   ├── Network/                     # InterfaceInfo, InterfaceEligibility, IpMath (masks, subnets)
│   ├── Dhcp/                        # DhcpMessage (parse/serialize), DhcpOptions, DhcpEngine
│   ├── Leases/                      # AddressPool, Lease, LeaseTable, LeaseFile (format only)
│   └── Runtime/                     # StartGate, ServerSession, ServerState, FailureReason
└── DhcpServer.App/
    ├── Infrastructure/              # SystemClock, NetworkInterfaceProvider, UdpEndpointFactory,
    │                                #   ArpIcmpProber (SendARP P/Invoke), DhcpServerProbe, FileLeaseStore
    ├── ViewModels/                  # MainViewModel (extended), InterfaceItemViewModel, LeaseItemViewModel
    ├── Views/MainWindow.xaml(.cs)   # extended: selector, Start/Stop, status, lease DataGrid
    └── Resources/Strings*.resx      # new keys in all six files
tests/
└── DhcpServer.Tests/
    ├── Dhcp/  Leases/  Network/  Runtime/  ViewModels/   # suites listed in quickstart.md
    └── Fakes/                       # FakeClock, FakeInterfaceProvider, FakeEndpoint, FakeProber, …
docs/manual-test-checklist.md        # scenarios from quickstart.md added
```

**Structure Decision**: keep the two existing projects and the existing test project; add folders
inside them. Real network/OS access lives in `DhcpServer.App/Infrastructure` (the only place that
touches sockets, NICs, `iphlpapi`, files), everything decidable is in Core and tested with fakes.

## Complexity Tracking

No constitution violations to justify.
