# Research: PC Address Settings

Builds on feature 001 (`specs/001-dhcp-lease-server/`): `ServerSession`, `StartGate`,
`InterfaceEligibility`, `IInterfaceProvider`, `PortableSettings`. Items marked **Validate** must be
confirmed on real Windows 7 SP1 and Windows 11 machines by the spike task.

## D1. Reading the current configuration: BCL + read-only registry

- **Decision**: Read addresses, masks, gateway, DNS and DHCP mode through
  `System.Net.NetworkInformation` (`GetIPProperties()`, `GetIPv4Properties().IsDhcpEnabled`,
  `.Index`). The BCL cannot tell whether the DNS servers were typed by the user or came from DHCP,
  so the *static* DNS list is read from the adapter's `NameServer` value under
  `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapter id}` (read-only).
- **Rationale**: no WMI, no output parsing, locale independent, same on Windows 7 – 11. Reading the
  registry does not violate Principle III (only *writing* is forbidden).
- **Alternatives**: WMI `Win32_NetworkAdapterConfiguration` (also cannot separate static/DHCP DNS,
  slow first call, extra COM surface); parsing `netsh ... show config` (localized text, rejected).
- **Validate**: `NameServer` is empty for "automatic DNS" and holds a comma/space separated list for
  static DNS on both OSes; adapters with a DHCP address but static DNS round-trip correctly.

## D2. Writing the configuration: `netsh` by interface index, exit codes only

- **Decision**: Changes are made with `%SystemRoot%\System32\netsh.exe interface ipv4 …`, started
  without a shell, addressing the adapter by its numeric interface index, and judged only by the
  process exit code (never by parsed text). Each command is one named *step* (data-model
  `ApplyStep`). Commands:

  | Step | Command (index = `<i>`) |
  |------|-------------------------|
  | `SetAddress` | `set address name=<i> source=static address=<ip> mask=<mask> gateway=<gw\|none>` |
  | `AddAddress` (restore of extra addresses) | `add address name=<i> address=<ip> mask=<mask>` |
  | `SetDns` | `set dnsservers name=<i> source=static address=<dns1> validate=no`, then `add dnsservers name=<i> address=<dns2> validate=no` |
  | `ClearDns` | `set dnsservers name=<i> source=static address=none validate=no` (empty static list) |
  | `SetAddressDhcp` | `set address name=<i> source=dhcp` |
  | `SetDnsDhcp` | `set dnsservers name=<i> source=dhcp` |

- **Rationale**: constitution "System changes" names `netsh` as an allowed mechanism that exists on
  Windows 7 SP1; one process per step maps 1:1 to the "exact failing step" requirement (FR-010) and
  needs no new dependency or COM. Arguments are built only from validated IPv4 values and a number
  (no user text reaches the command line) — no injection surface.
- **Alternatives**: WMI `EnableStatic`/`SetGateways`/`SetDNSServerSearchOrder` (clearing a gateway
  via WMI is unreliable); `Set-NetIPAddress` cmdlets (Windows 8+ only); the `iphlpapi` `*UnicastIpAddressEntry` family
  (Vista+ but a lot of interop for little gain).
- **Validate**: `gateway=none` and `address=none` forms are accepted on Windows 7 SP1 and 11; `set
  address source=static` replaces all static addresses of the adapter (otherwise a `DeleteAddress`
  step is added to the plan before implementation).

## D3. Verification after every apply and restore

- **Decision**: After the last step, re-read the adapter (D1) and compare with the target
  (addresses, mask, gateway, DNS list; DHCP flag). A mismatch is a failure of the synthetic step
  `Verify`. The comparison is the same code used for the "someone else changed it" check (D6).
- **Rationale**: exit code 0 from `netsh` does not prove the final state; verification makes SC-003
  testable and makes partial states visible.

## D4. Waiting for the new address to become usable

- **Decision**: After `SetAddress`, poll the adapter up to 10 s (every 250 ms) until the address is
  reported with `DuplicateAddressDetectionState == Preferred` (`UnicastIPAddressInformation`); on
  timeout the step `WaitForAddress` fails and the apply is rolled back.
- **Rationale**: binding UDP 67 to a *tentative* address fails with "address not available";
  waiting is also what covers SC-007 (< 10 s). Windows runs duplicate-address detection after
  assigning a static address.
- **Validate**: typical wait on Windows 7 and 11; on a link-down adapter the address stays
  non-preferred — but Start is refused earlier for a down adapter (D8), so this path is rare.

## D5. Saved configuration and crash recovery

- **Decision**: Before the first command the complete previous configuration plus the target are
  written to `DhcpServer.restore` next to the executable (format: `contracts/restore-file.md`,
  atomic temp-file replace, flushed to disk). The file is deleted only after a successful, verified
  restore. At program start, if the file exists, recovery runs *before* the UI allows a session:
  restore → success: notice "settings restored"; failure: banner with **Retry** / **Discard**.
- **Rationale**: Principle II: "restored on stop, on exit, and — best effort — at the next start
  after a crash". File-next-to-exe is allowed by Principle III; if it cannot be written, nothing is
  changed (FR-014): the writability check is a real write of the record before step 1.
- **Alternatives**: store in `DhcpServer.ini` (mixes persistent preferences with crash state, and
  `PortableSettings` saving is deliberately non-fatal — here a failed write must be fatal for apply).
- **Power loss**: the record is fsync'd before the first change; after reboot the file exists and
  recovery runs as above.

## D6. Restoring only when nothing else changed the adapter

- **Decision**: At restore time re-read the adapter and compare with the *applied* configuration
  stored in the record. Equal → restore. Different (user or another tool changed it) → leave the
  adapter alone, delete the record, tell the user (FR-009). Adapter not found → keep the record,
  tell the user, retry at next start.
- **Rationale**: matches the spec assumption ("manual change wins"); avoids clobbering a deliberate
  fix. A DHCP-style adapter that was restored to automatic may have a different dynamic address than
  before; that is expected and not compared.

## D7. One instance at a time (FR-015)

- **Decision**: A named mutex `Global\DhcpServer.AddressSettings` is held from the first change
  until the restore finished (and during startup recovery). A second instance that cannot take it
  refuses to apply/restore and reports `SettingsLocked`. Two instances serving the same interface
  are already prevented by the exclusive bind of UDP 67 (feature 001).
- **Rationale**: a mutex is a kernel object, not a registry/file artifact (Principle III).
- **Alternatives**: lock file (survives crashes → needs staleness logic); single-instance for the
  whole app (broader than required, annoying for a diagnostic tool).

## D8. Interaction with interface eligibility (feature 001)

- **Decision**: Split "can be chosen" from "can serve as-is":
  - `InterfaceEligibility.Evaluate` stays unchanged (it is the serving gate, Principle I).
  - New `InterfaceEligibility.IsConfigurable(info)`: type is Ethernet/Wireless (any status, any
    address). The selector enables such adapters and marks those that are not eligible as *needs
    address*; `NotSupportedType` adapters stay disabled.
  - Start refuses an adapter that is **Down** before changing anything (`Reason_Down`), even in the
    configure path (spec edge case).
  - Overlap: for the configure path, overlap is evaluated against the *proposed* subnet (D9), not
    the adapter's current one.
- **Rationale**: keeps all safety rules of Principle I intact; only the entry point changes.

## D9. Validation and suggestion rules

- **Validation** (pure, in Core): address is well-formed IPv4, not loopback, link-local,
  multicast/reserved (≥ 224), `0.x`; mask is contiguous with prefix 8–30; address ≠ network and ≠
  broadcast of the mask; gateway empty or inside the subnet and ≠ address/network/broadcast; up to
  two DNS servers, each a valid unicast IPv4, no duplicates; proposed subnet does not overlap any
  other active non-loopback interface.
- **Suggestion**: first of `192.168.137.1/24`, then `192.168.138.1/24 … 192.168.199.1/24`, then
  `10.137.0.1/24`, that does not overlap any other active interface; when an `IAddressProber` is
  available the candidate address itself is probed once (ARP/ping) and skipped if it answers.
  Suggested gateway/DNS are empty (no routing implied; Internet sharing is a separate feature).
- **Rationale**: `192.168.137.0/24` is also the Windows ICS default, so a later sharing feature
  needs no change; a deterministic list keeps tests simple.

## D10. Orchestration and state

- **Decision**: New Core service `AddressConfigurator` (apply/restore/recover) behind
  `IAdapterConfigService` (read + step execution), `IRestoreStore` and `ISettingsLock`. The
  confirmation preview is built by Core (`ChangePreview`) and *shown by the view model*; the session
  never blocks on UI. `ServerSession.StartAsync(chosen, AddressSettings optional)`: when settings are
  given the sequence is lock → capture → write record → apply → verify → wait for address → (gate
  as in 001) → bind; any failure rolls back and ends in `Failed`. `Stop`/`Dispose`/interface loss
  close the endpoint first, then restore, then state `Stopped`. A new transient state `Stopping`
  covers restore (up to ~10 s) so the UI can show progress.
- **Rationale**: keeps the UI thin (Principle VI), makes every Principle II rule unit-testable with
  fakes (fake adapter service that can fail at any step).

## D11. Windows 7 / elevation notes

- `netsh interface ipv4` and `NetworkInterface` are available on Windows 7 SP1; the manifest already
  requires administrator. No firewall, service, driver or registry *writes* are introduced.
- The adapter is addressed by interface index, which can change after a driver reinstall; the record
  stores both the adapter GUID (`NetworkInterface.Id`) and the index and resolves by GUID first.

## D12. Localization impact

- About 65 new string keys (`contracts/ui-contract.md`), each in all six `Strings*.resx` files
  with real translations; `LocalizationTests` enforces parity.

## Spike results (T001, read-only)

| Date | OS | Check | Result |
|------|----|-------|--------|
| 2026-11-05 | Windows 11 Pro (build 26300) | Registry `NameServer` vs `DhcpNameServer` vs BCL `DnsAddresses` on 9 adapters | **Confirms D1**: for the DHCP adapter `NameServer` is empty and `DhcpNameServer` holds the leased DNS, while the BCL reports the effective DNS only. `NameServer` is the right source for the *static* DNS list. |
| 2026-11-05 | same | BCL `DnsAddresses` content | Also lists IPv6 site-local entries (`fec0:0:0:ffff::1…3`) for adapters without DNS — **only IPv4 entries may be used**. |
| 2026-11-05 | same | `GetIPv4Properties()` | Throws `NetworkInformationException` for an adapter without an IPv4 stack binding — callers must catch it and treat the adapter as "no IPv4 configuration" (not configurable). |
| 2026-11-05 | same | Duplicate-address-detection state | `Preferred` on Up adapters, `Tentative` on Down adapters — confirms D4 (wait for `Preferred`) and that a Down adapter never becomes usable. |
| 2026-11-05 | same | `netsh interface ipv4 set address ?` / `set dnsservers ?` | `gateway=<ip>\|none`, `address=<ip>\|none`, `validate=yes\|no`, `source=dhcp\|static` all documented — **confirms D2 command forms**. `name=` is documented as an interface name; accepting the numeric index is verified in the manual scenarios (T052). |
| open | Windows 7 SP1 | same checks | **Not yet verified** — manual scenarios (T052). |
| open | any | mutating checks: `set address source=static` replaces all static addresses? time to `Preferred`? index accepted for `name=`? | **Not yet verified** — manual scenarios on a spare adapter (T052). |
