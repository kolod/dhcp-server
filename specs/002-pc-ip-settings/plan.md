# Implementation Plan: PC Address Settings

**Branch**: `002-pc-ip-settings` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-pc-ip-settings/spec.md`

## Summary

Let the user give the chosen adapter a fixed IPv4 address (address, mask, optional gateway and up to
two DNS servers) as part of Start, after seeing and confirming a side-by-side preview, and put the
adapter back exactly as it was on Stop, exit, interface loss and — after a crash or power loss — at
the next program start. Approach (see [research.md](research.md)): read the configuration through
the BCL plus a read-only registry value, write it with `netsh interface ipv4` steps judged by exit
codes, verify after every apply/restore, persist a restore record next to the executable *before*
the first change, guard with a named mutex, and keep all decisions (validation, suggestion,
preview, apply/restore/recovery sequences) in `DhcpServer.Core` behind small abstractions.

## Technical Context

**Language/Version**: C# 7.3

**Primary Dependencies**: none beyond the BCL (`System.Net.NetworkInformation`, `System.Diagnostics.Process` for `netsh.exe`, `Microsoft.Win32.Registry` read-only, `System.Threading.Mutex`)

**Storage**: `DhcpServer.restore` next to the executable ([contracts/restore-file.md](contracts/restore-file.md)); `DhcpServer.ini` unchanged

**Testing**: xUnit on `net48` with fakes (`IAdapterConfigService` that can fail at any step, `IRestoreStore`, `ISettingsLock`); manual scenarios in [quickstart.md](quickstart.md)

**Target Platform**: Windows 7 SP1 – 11, x86/x64, .NET Framework 4.8, WPF software-rendering safe, administrator

**Project Type**: desktop-app (single portable executable)

**Performance Goals**: apply and restore each < 10 s (incl. ≤ 10 s wait for the address to become usable); preview/validation instant

**Constraints**: no registry writes, no firewall/service/driver changes, only the chosen adapter touched, record must be on disk before the first change, executable size < 5 MB, no new DLLs

**Scale/Scope**: one adapter at a time; IPv4 only; ≤ 8 saved addresses per adapter (more is shown as unsupported and refused)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the plan satisfies it |
|-----------|--------|---------------------------|
| I. Network Safety First | PASS | Serving gates of feature 001 are unchanged; the new path re-runs them after applying (eligibility, other-server probe, bind). The proposed subnet is checked against other active interfaces *before* applying (D9). A Down adapter is refused before any change (D8). |
| II. Reversible System Changes | PASS (this feature is Principle II) | Preview + explicit confirmation (FR-004); previous configuration captured and persisted before step 1 (D5); restore on stop, exit, interface loss and next start after a crash (apply-sequence.md); failures name the exact step and never leave the server running; only the chosen adapter changes (FR-012); no firewall/service/driver/registry writes. Each rule has automated tests (quickstart table). |
| III. Portable & Compact | PASS | One extra file beside the exe (written only when needed); no dependencies; reading the registry is read-only; the mutex is a kernel object. |
| IV. Windows 7 – 11 | PASS | `netsh interface ipv4` and `NetworkInterface` exist on Windows 7 SP1; nothing Windows 10-only. **Validate** list in research D1/D2/D4 on Windows 7 SP1. |
| V. English-first / Localized | PASS | ~60 new keys in all six `.resx` ([ui-contract.md](contracts/ui-contract.md)); `netsh` output is never shown or parsed; layouts auto-sized. |
| VI. Testable Core | PASS | Validation, suggestion, preview, configurator, recovery in Core behind interfaces; `netsh`, registry, file and mutex in `App/Infrastructure`; view models stay MVVM. |
| VII. Simplicity | PASS | Part of the stated scope ("configuring the PC's own IP address"); no framework, no new abstractions beyond the three needed for fakes; Internet sharing and anything routing-related stays out. |

No violations; Complexity Tracking not needed.

**Post-design re-check (after Phase 1)**: PASS. Open validation items, not violations: `netsh`
argument forms on Windows 7 (D2), static-vs-DHCP DNS detection via the registry (D1), duplicate-address
detection wait (D4).

## Project Structure

### Documentation (this feature)

```text
specs/002-pc-ip-settings/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── apply-sequence.md
│   ├── restore-file.md
│   └── ui-contract.md
├── checklists/requirements.md
└── tasks.md             # produced by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DhcpServer.Core/
│   ├── Abstractions/     # + IAdapterConfigService, IRestoreStore, ISettingsLock
│   ├── Network/          # InterfaceEligibility (+ IsConfigurable)
│   ├── Addressing/       # AddressSettings, AddressSettingsValidator, AddressSuggester,
│   │                     #   AdapterConfig, SavedConfiguration, RestoreFile (format),
│   │                     #   ChangePreview, ApplyStep/StepResult, AddressConfigurator, RecoveryService
│   └── Runtime/          # ServerSession (Start with settings, Stopping, restore), FailureReason (+kinds), ServerState (+Stopping)
└── DhcpServer.App/
    ├── Infrastructure/   # NetshAdapterConfigService, FileRestoreStore, NamedMutexSettingsLock
    ├── ViewModels/       # MainViewModel (+ address group), AddressSettingsViewModel, ConfirmChangesViewModel, RecoveryViewModel
    ├── Views/            # MainWindow.xaml (address group, recovery banner), ConfirmChangesWindow.xaml
    └── Resources/Strings*.resx
tests/
└── DhcpServer.Tests/
    ├── Addressing/       # suites listed in quickstart.md
    ├── Runtime/          # ServerSessionAddressTests
    └── Fakes/            # FakeAdapterConfigService, InMemoryRestoreStore, FakeSettingsLock
docs/manual-test-checklist.md   # scenarios from quickstart.md added
```

**Structure Decision**: same two projects plus the test project; a new `Addressing` folder in Core
holds all decisions; the only OS access (`netsh`, registry read, file, mutex) is in
`App/Infrastructure`, mirroring feature 001.

## Complexity Tracking

No constitution violations to justify.
