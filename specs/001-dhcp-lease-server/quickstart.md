# Quickstart: validating the DHCP Lease Server

## Prerequisites

- Windows 7 SP1 – 11 PC, administrator rights, .NET Framework 4.8.
- Build tooling: .NET SDK per `global.json`; Pester 5 for `tools/tests`.
- For manual scenarios: a second device or PC connected by cable (or a USB Ethernet adapter on a
  laptop), set to "obtain an IP address automatically", plus the laptop's adapter set to a static
  address such as `192.168.137.1/24` (configuring it is out of scope for this feature).

## Automated checks

```powershell
dotnet build DhcpServer.slnx -c Release -warnaserror
dotnet test tests/DhcpServer.Tests -c Release --no-build
pwsh tests/check-artifact.ps1
```

Expected: build clean; all tests pass, including the new suites below; artifact check confirms a
single `DhcpServer.exe` with no new DLLs, size < 5 MB, manifest `requireAdministrator`.

| Suite (tests/DhcpServer.Tests) | Proves |
|--------------------------------|--------|
| `Dhcp/DhcpMessageTests` | parse/serialize round-trip, malformed input rejected (FR-017) |
| `Dhcp/DhcpEngineTests` | DISCOVER→OFFER→REQUEST→ACK, renewal, release, decline, NAK cases (FR-008) |
| `Leases/AddressPoolTests` | server/gateway/network/broadcast never leased (FR-005) |
| `Leases/LeaseTableTests` | same MAC → same address, no duplicates, expiry, reuse order, exhaustion (FR-007/009/016) |
| `Leases/LeaseFileTests` | round-trip, corrupt lines skipped, 1 024 cap, non-fatal IO errors (FR-013) |
| `Network/InterfaceEligibilityTests` | every unavailability reason (FR-003, Principle I) |
| `Runtime/ServerSessionTests` | refuses without interface / with other DHCP server / inconclusive probe / overlap; binds only the chosen interface; stops on interface loss; no listener after stop (FR-001/002/004/014/018) |
| `Runtime/UsedAddressTests` | address answering ARP/ping is not offered (FR-006) |
| `ViewModels/MainViewModelTests` | state text, selection memory, lease list updates (FR-010/011/015) |
| `Localization/LocalizationTests` | every new key present in all six languages (FR-019) |

## Manual scenarios (map to `docs/manual-test-checklist.md`, run on Windows 7 SP1 and Windows 11)

1. **Happy path (US1, SC-001/002)** — start `DhcpServer.exe` (accept UAC and the firewall prompt for
   the network type shown), pick the cable interface, press Start. Plug in / reboot the device. It
   gets an address in `192.168.137.2+`, and `ping` both ways works.
2. **Lease list (US2, SC-003/008)** — the device appears in the list within a second with IP, MAC,
   host name. Renew (`ipconfig /renew` on the other PC): no duplicate row. `ipconfig /release`: row
   becomes *Released*. Copy IP via Ctrl+C and the context menu.
3. **Persistence (FR-013)** — stop, close, reopen: rows are still listed as *Inactive*; restart the
   server; the same device gets the same address.
4. **Wrong interface (US3, SC-004)** — select an adapter on a network with a DHCP server (e.g. the
   router LAN): start is refused with the "another DHCP server" reason, nothing is bound
   (`netstat -ano -p udp` shows no `:67` for the app).
5. **Interface down (US3)** — pull the cable while running: the server stops within 2 s and tells why.
6. **Overlap / no address / link-local (US3, US4)** — adapters shown unavailable with the reason.
7. **Isolation (SC-005)** — with a second adapter connected to another host, that host gets no answer.
8. **Exit (SC-007)** — close the window while running: `netstat` shows no listener, the device
   cannot renew.
9. **Languages (FR-019)** — switch each of the six languages at runtime; no clipping in the status
   area and lease columns.
10. **Software rendering (Principle IV)** — run in a VM / Remote Desktop session: window renders and
    the list updates.

Record results in the release checklist together with the SHA-256 of the executable.
