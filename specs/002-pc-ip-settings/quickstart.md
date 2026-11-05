# Quickstart: validating PC Address Settings

## Prerequisites

- Feature 001 merged (this branch is based on it). Windows 7 SP1 – 11, administrator rights.
- A USB Ethernet adapter or a spare wired adapter to experiment on — **never use the adapter that
  carries your normal network**. Write down its current settings first (`ipconfig /all`).
- Build/test commands as in `CLAUDE.md`.

## Automated checks

```powershell
dotnet build DhcpServer.slnx -c Release -warnaserror
dotnet test tests/DhcpServer.Tests -c Release --no-build
pwsh tests/check-artifact.ps1
```

| Suite (tests/DhcpServer.Tests) | Proves |
|--------------------------------|--------|
| `Addressing/AddressSettingsValidatorTests` | every rule of research D9, one test per `Field_<Kind>` (FR-003) |
| `Addressing/AddressSuggesterTests` | first non-overlapping candidate, skips probed-in-use, deterministic (FR-002) |
| `Addressing/AdapterConfigTests` | equivalence rules, `FromSettings`, DHCP vs static comparison |
| `Addressing/ChangePreviewTests` | rows and changed flags for static→static, DHCP→static, DNS added/removed (FR-004) |
| `Addressing/RestoreFileTests` | round-trip, `restoring=1`, corrupt/missing keys, multiple addresses and DNS (FR-006/013) |
| `Addressing/AddressConfiguratorTests` | apply order, record written *before* step 1, no record ⇒ no change (FR-014), rollback after a failure at **each** step 4–8 (FR-011), restore for static/DHCP/multi-address/with and without gateway+DNS (SC-003), "changed by someone else" leaves the adapter alone (FR-009), adapter missing keeps the record, failure names the step (FR-010), only the chosen adapter id is ever passed (FR-012), lock refusal (FR-015) |
| `Addressing/RecoveryTests` | simulated crash (record present at start) restores before any session can start; failed recovery blocks Start until Retry/Discard (SC-004) |
| `Runtime/ServerSessionAddressTests` | Start with settings: order of record → apply → gate → bind; failure ⇒ never running; Stop/exit/interface-lost close the endpoint before restoring; state `Stopping`; down adapter refused before any change (spec edge case) |
| `Network/InterfaceEligibilityTests` (extended) | `IsConfigurable` for each kind/status; `Evaluate` unchanged |
| `ViewModels/AddressSettingsViewModelTests` | checkbox rules, prefill/suggestion, inline errors, Start enable, preview + confirm/cancel flow (nothing applied before confirm, SC-002), recovery banner Retry/Discard |
| `Localization/LocalizationTests` | every new key in all six languages |

## Manual scenarios (added to `docs/manual-test-checklist.md`; Windows 7 SP1 and Windows 11)

1. **Automatic → fixed (SC-001/SC-003).** Spare adapter on "obtain automatically", cable to a test
   device. Select it: checkbox on, suggestion shown. Press Start, read the preview, confirm. The
   adapter has the new address (`ipconfig /all`), the server runs, the device gets an address.
   Press Stop: `ipconfig /all` shows automatic mode again.
2. **Static → static with gateway and DNS.** Give the adapter a fixed address, gateway and two DNS
   servers first. Run a session with other settings, stop: the original address, gateway and both
   DNS servers are back, including a second address if you configured one.
3. **Cancel (SC-002).** Press Cancel in the preview: nothing changed (`ipconfig /all`), server not started.
4. **Invalid input.** Try a malformed address, `255.0.255.0`, a /31, a gateway in another subnet,
   an address in another connected adapter's subnet: inline messages, Start disabled.
5. **Crash (SC-004).** Start a session, then end the process in Task Manager. The adapter keeps the
   session address. Start the program again: it restores the old settings first and says so.
6. **Power loss.** Same as 5 with a VM hard reset.
7. **Changed by someone else (FR-009).** Start a session, change the adapter's address in the
   Windows dialog, Stop: the program leaves your change and tells you.
8. **Adapter missing.** Start a session with the USB adapter, unplug it, Stop/close: the program
   says it could not restore; replug and restart: Retry restores.
9. **Failure.** With a deliberately blocked `netsh` (e.g. adapter disabled mid-apply) the message
   names the step and the adapter is back to the old settings; the server is not running.
10. **Read-only folder (FR-014).** Run from a read-only folder: choosing a fixed address is refused
    with `Reason_RecordNotWritable` and nothing changes.
11. **Two instances (FR-015).** While a session with a fixed address runs, start a second instance
    and try the same: it refuses with `Reason_SettingsLocked`.
12. **Languages.** All new texts in the six languages without clipping; confirmation dialog included.

Record results with the SHA-256 of the executable in the release checklist.
