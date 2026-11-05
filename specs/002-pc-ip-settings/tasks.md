---

description: "Task list for the PC Address Settings feature"
---

# Tasks: PC Address Settings

**Input**: Design documents from `/specs/002-pc-ip-settings/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (apply-sequence.md, restore-file.md, ui-contract.md), quickstart.md. Feature 001 (`specs/001-dhcp-lease-server/`) is merged and provides `ServerSession`, `StartGate`, `InterfaceEligibility`, `IInterfaceProvider`, the view models and the fakes in `tests/DhcpServer.Tests/Fakes/`.

**Tests**: INCLUDED. Principle VI requires an automated test for every rule of Principle II, which is the whole point of this feature. Test tasks come before the implementation they cover; write them first and see them fail.

**Organization**: Grouped by user story. US1 (apply) and US2 (restore + crash recovery) are P1, US3 (failure handling and rollback) is P2. **US1 alone changes the PC's network settings without a safe way back — do not run it on a machine you care about, and never release before US1–US3 are complete** (Principle II). Do US2 immediately after US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: US1–US3 from spec.md
- Paths are relative to the repository root. `Core` = `src/DhcpServer.Core`, `App` = `src/DhcpServer.App`, `Tests` = `tests/DhcpServer.Tests`
- Rules for every task: English only in code/comments/docs; C# 7.3; no user-visible text in code or XAML; every new string key goes into **all six** `App/Resources/Strings*.resx` (en, uk, de, fr, es, ja) with a real translation (`tasks` below list the keys); build must pass `-warnaserror`; Core never references WPF; no registry writes, no firewall/service/driver changes; `netsh` is never parsed, only its exit code is used.
- **Safety while developing**: never run the real apply/restore code against the adapter that carries your normal network. Use a spare USB Ethernet adapter or a VM, and note its settings first.

## Phase 1: Setup

**Purpose**: De-risk the `netsh`/registry assumptions without changing anything, and keep runtime files out of git.

- [X] T001 Read-only spike (not shipped): in the session scratchpad write a throw-away script/console program that, on Windows 11 and Windows 7 SP1, (a) prints the `NameServer` and `DhcpNameServer` values under `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\<adapter GUID>` for every adapter next to what `NetworkInterface.GetIPProperties().DnsAddresses` reports (research D1), (b) prints `netsh interface ipv4 set address ?`, `set dnsservers ?`, `add address ?` help to confirm the `gateway=none`, `address=none`, `validate=no`, `source=dhcp` forms (research D2), (c) lists `UnicastIPAddressInformation.DuplicateAddressDetectionState` of every address (research D4). **Do not run any command that changes an adapter.** Record the outcome (OS, results, any deviation) in a new "Spike results" section at the end of `specs/002-pc-ip-settings/research.md`; the mutating checks (does `set address source=static` replace all static addresses, how long until `Preferred`) are manual scenarios 1–2 of quickstart.md on a spare adapter (T052). If a form is not accepted, amend research D2 and `contracts/apply-sequence.md` before starting US1
- [X] T002 [P] Add `DhcpServer.leases`, `DhcpServer.leases.tmp`, `DhcpServer.restore` and `DhcpServer.restore.tmp` to the "Local runtime files" block of `.gitignore` (today only `DhcpServer.ini` is listed, so a lease file from feature 001 could be committed by accident)

**Checkpoint**: Platform assumptions recorded; runtime files ignored.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Pure Core types, abstractions and fakes used by every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create the abstractions in `src/DhcpServer.Core/Abstractions/`: `IAdapterConfigService.cs` (`AdapterConfig Read(string adapterId)` returning null when the adapter does not exist; `StepResult Execute(ApplyStep step, AdapterConfig target)` performing one named step on `target.AdapterId`), `IRestoreStore.cs` (`SavedConfiguration Load()` returning null when absent and throwing `RestoreRecordCorruptException` when unreadable; `void Save(SavedConfiguration)` that **throws** on any failure and flushes to disk; `void Delete()`), `ISettingsLock.cs` (`bool TryAcquire()`, `void Release()`); and `src/DhcpServer.Core/Addressing/ApplyStep.cs` (enum `Capture, WriteRecord, SetAddress, AddAddress, SetDns, ClearDns, SetAddressDhcp, SetDnsDhcp, Verify, WaitForAddress, DeleteRecord` and `StepResult` with `Step`, `Succeeded`, `ExitCode`), per data-model.md
- [X] T004 [P] Extend `src/DhcpServer.Core/Runtime/FailureReason.cs` with `FailureKind` values `AddressApplyFailed, AddressRestoreFailed, SettingsLocked, RecordNotWritable, AddressNotReady` (resource keys stay `Reason_<Kind>`; `Argument` carries the step name) and `src/DhcpServer.Core/Runtime/ServerState.cs` with `Stopping`; fix any `switch` over `ServerState` in `src/DhcpServer.App/ViewModels/MainViewModel.cs` so it still compiles (status text for `Stopping` comes in US2)
- [X] T005 [P] Add `IsAddressPreferred` (bool, default true) to `src/DhcpServer.Core/Network/InterfaceInfo.cs` (optional last constructor parameter, so existing callers and tests keep compiling) and fill it in `src/DhcpServer.App/Infrastructure/NetworkInterfaceProvider.cs` from the chosen address' `DuplicateAddressDetectionState == Preferred`
- [X] T006 [P] Write failing tests `tests/DhcpServer.Tests/Addressing/AdapterConfigTests.cs`: `Equivalent` for static vs static (set equality of addresses incl. prefix, gateway, DNS order), DHCP vs DHCP ignores the leased address, mode mismatch, DNS static vs automatic mismatch, `FromSettings` builds one static address + optional gateway + static DNS (or *static empty* DNS when none)
- [X] T007 Implement `src/DhcpServer.Core/Addressing/AddressSettings.cs` (address, prefix, optional gateway, 0–2 DNS) and `src/DhcpServer.Core/Addressing/AdapterConfig.cs` (adapter id, interface index, address list, gateway, DNS list, `AddressMode`, `DnsMode`, `Equivalent`, `FromSettings`) to pass T006
- [X] T008 [P] Write failing tests `tests/DhcpServer.Tests/Addressing/AddressSettingsValidatorTests.cs`: one test per `FieldError.Kind` of data-model.md (`Required, Malformed, NotUnicast, LinkLocal, MaskInvalid, MaskSize, NetworkOrBroadcast, GatewayOutsideSubnet, DnsDuplicate, SubnetOverlap`), valid minimal and full settings, overlap judged against the **proposed** subnet and ignoring the chosen adapter itself and Down/loopback interfaces, mask `255.0.255.0` invalid, /7 and /31 rejected, gateway equal to the address rejected (FR-003)
- [X] T009 Implement `src/DhcpServer.Core/Addressing/AddressSettingsValidator.cs` (`Validate(settings text fields or AddressSettings, chosenInterfaceId, allInterfaces)` → `IReadOnlyList<FieldError>`; accepts raw field strings so the view model can show `Malformed`/`Required`) using `IpMath`, to pass T008
- [X] T010 [P] Write failing tests `tests/DhcpServer.Tests/Addressing/AddressSuggesterTests.cs`: returns `192.168.137.1/24` when free; skips candidates overlapping another active interface; skips a candidate whose address answers the fake `IAddressProber`; deterministic order `192.168.138.1…192.168.199.1`, then `10.137.0.1/24`; returns null when everything is taken; suggested gateway and DNS empty (research D9)
- [X] T011 Implement `src/DhcpServer.Core/Addressing/AddressSuggester.cs` to pass T010
- [X] T012 [P] Write failing tests `tests/DhcpServer.Tests/Addressing/ChangePreviewTests.cs`: static→static (changed flags per row), DHCP→static, static→static with DNS added/removed, gateway added/removed, unchanged rows not flagged, rows in the order Mode, Address, Mask, Gateway, DNS, no localized text in Core (markers only) (FR-004)
- [X] T013 Implement `src/DhcpServer.Core/Addressing/ChangePreview.cs` (`Create(AdapterConfig current, AddressSettings proposed)`, rows of label key / current / new / changed, with `Automatic`/`None` markers) to pass T012
- [X] T014 [P] Write failing tests `tests/DhcpServer.Tests/Addressing/RestoreFileTests.cs` against `contracts/restore-file.md`: round-trip with several addresses and DNS servers, DHCP previous mode (no address lines), `restoring=1` marker, wrong header ⇒ corrupt, missing `adapter`/`previous.mode`/`applied.mode` ⇒ corrupt, unknown keys ignored, `\r\n` and `\n` input
- [X] T015 Implement `src/DhcpServer.Core/Addressing/SavedConfiguration.cs` (previous, applied, created UTC, `Restoring` flag) and `src/DhcpServer.Core/Addressing/RestoreFile.cs` (`Serialize`, `Parse` throwing `RestoreRecordCorruptException`) to pass T014
- [X] T016 [P] Write failing tests `tests/DhcpServer.Tests/Network/InterfaceConfigurableTests.cs`: `IsConfigurable` true for Ethernet/Wireless in every status and address state (down, no address, link-local, /31, overlapping), false for loopback/tunnel/other; `Evaluate` results unchanged (existing `InterfaceEligibilityTests` must stay green)
- [X] T017 Add `IsConfigurable(InterfaceInfo)` to `src/DhcpServer.Core/Network/InterfaceEligibility.cs` without changing `Evaluate` (research D8) to pass T016
- [X] T018 Create the addressing fakes in `tests/DhcpServer.Tests/Fakes/AddressingFakes.cs`: `FakeAdapterConfigService` (in-memory `AdapterConfig` per adapter id; `Execute` logs `(step, adapterId)` and *applies the step's effect* to the in-memory config so `Verify`/restore can be asserted; can be told to fail at a given step the n-th time; can remove an adapter; can simulate "someone else changed it"), `InMemoryRestoreStore` (records `Save`/`Delete` order into a shared log, can throw on `Save`, can hold a corrupt record), `FakeSettingsLock` (can refuse), and a shared `List<string>` event log so tests can assert order across fake service, store, endpoint factory and probe

**Checkpoint**: Foundation ready — all pure types built and tested; stories can start.

---

## Phase 3: User Story 1 - Give the PC a fixed address for the session (Priority: P1) 🎯 MVP

**Goal**: The user picks an adapter, edits a suggested address/mask/gateway/DNS, reviews and confirms the preview, presses Start, and the server runs on the adapter with the new settings.

**Independent Test**: With the fake adapter service an automatic adapter ends up with the chosen static settings, the record is on disk before the first step, nothing changes before confirmation; manually, quickstart scenarios 1, 3 and 4 on a spare adapter.

### Tests for User Story 1

- [X] T019 [P] [US1] Write failing tests `tests/DhcpServer.Tests/Addressing/AddressConfiguratorTests.cs` (apply region): step order `Lock → Capture → WriteRecord → SetAddress → SetDns/ClearDns → Verify → WaitForAddress`; `WriteRecord` happens before the first `Execute` (shared event log); store that throws on `Save` ⇒ **no** adapter step executed and result `RecordNotWritable` (FR-014); lock refused ⇒ nothing executed and `SettingsLocked` (FR-015); pending record present ⇒ refused; only the chosen adapter id is ever passed to the service (FR-012); `WaitForAddress` polls `IInterfaceProvider` until `IsAddressPreferred` (fake sleeper/clock) and times out after 10 s with `AddressNotReady`; verify compares with the target
- [X] T020 [P] [US1] Write failing tests `tests/DhcpServer.Tests/Runtime/ServerSessionAddressTests.cs` (start region): `StartAsync(chosen, settings)` sequence is record → apply → start gate (interface re-read, eligibility, probe) → bind; a Down adapter is refused with `Reason_Down` before any step or record (spec edge case); adapter that cannot serve as it is now starts when settings are given; with `settings == null` behavior is exactly feature 001 (all existing `ServerSessionTests` keep passing); the session is never `Running` before apply and verification finished
- [X] T021 [P] [US1] Write failing tests `tests/DhcpServer.Tests/ViewModels/AddressSettingsViewModelTests.cs`: checkbox on and locked when the adapter cannot serve as it is, off when it can (fields show current values read-only); suggestion prefilled once and never overwritten after the user typed; changing the adapter re-suggests; inline `Field_<Kind>` message per invalid field; Start disabled while invalid or Down; Start shows the preview through a fake `IChangeConfirmation` and **nothing** is applied before it returns true, Cancel ⇒ no service call and no start (SC-002); keeping a usable existing address starts without preview (FR-005); adapters that need an address are selectable and marked `Interface_NeedsAddress`, unsupported types stay unselectable

### Implementation for User Story 1

- [X] T022 [US1] Implement `src/DhcpServer.Core/Addressing/AddressConfigurator.cs`, apply part: ctor takes `IAdapterConfigService`, `IRestoreStore`, `ISettingsLock`, `IInterfaceProvider`, `IClock` and an injectable sleeper; `ApplyResult Apply(InterfaceInfo adapter, AddressSettings settings)` runs exactly the table in `contracts/apply-sequence.md` steps 0–7 (rollback is added in US3: until then a failure releases the lock and returns the failing step); never throws for expected failures; passes T019
- [X] T023 [US1] Extend `src/DhcpServer.Core/Runtime/ServerSession.cs` and `StartGate.cs`: ctor gets the `AddressConfigurator`; `StartAsync(InterfaceInfo chosen, AddressSettings settings = null)`; with settings: pre-checks (adapter Down ⇒ `Reason_Down` before anything changes), then `Apply`, then the unchanged gate (eligibility re-evaluated on the freshly read interface) and bind; failures map to `FailureReason(AddressApplyFailed, stepName)` / `RecordNotWritable` / `SettingsLocked` / `AddressNotReady`; update the `ServerSession` constructions in `Tests/Fakes` and existing tests (`SessionFixture`) accordingly; passes T020
- [X] T024 [P] [US1] Implement `src/DhcpServer.App/Infrastructure/NetshAdapterConfigService.cs`: `Read` via `NetworkInterface` (addresses, prefix, gateway, `IsDhcpEnabled`, interface index) plus read-only `NameServer` registry value for static DNS (research D1; null when the adapter does not exist); `Execute` builds the exact commands of research D2 from validated values and the interface index only, runs `%SystemRoot%\System32\netsh.exe` with `UseShellExecute=false`, no window, 10 s timeout per command, returns `StepResult` from the exit code only (never reads or shows output); `Verify` and `WaitForAddress` are implemented by the configurator, not here
- [X] T025 [P] [US1] Implement `src/DhcpServer.App/Infrastructure/FileRestoreStore.cs` (`DhcpServer.restore` next to the executable, atomic temp-file replace, `FileStream.Flush(true)`, `Save` throws on any failure, `Load` throws `RestoreRecordCorruptException` for unreadable content, `Delete` removes file and temp) and `src/DhcpServer.App/Infrastructure/NamedMutexSettingsLock.cs` (`Global\DhcpServer.AddressSettings`, `TryAcquire` non-blocking, treats an abandoned mutex as acquired, `Release` idempotent)
- [X] T026 [P] [US1] Add US1 string keys to all six `src/DhcpServer.App/Resources/Strings*.resx`: `Address_UseFixed`, `Address_Address`, `Address_Mask`, `Address_Gateway`, `Address_Dns1`, `Address_Dns2`, `Address_Optional`, `Interface_NeedsAddress`, `Field_Required`, `Field_Malformed`, `Field_NotUnicast`, `Field_LinkLocal`, `Field_MaskInvalid`, `Field_MaskSize`, `Field_NetworkOrBroadcast`, `Field_GatewayOutsideSubnet`, `Field_DnsDuplicate`, `Field_SubnetOverlap`, `Confirm_Title`, `Confirm_Intro` ({0} adapter), `Confirm_Setting`, `Confirm_Current`, `Confirm_New`, `Confirm_Apply`, `Confirm_RestoreNote`, `Confirm_Mode`, `Confirm_Automatic`, `Confirm_Static`, `Confirm_None`, `Notice_SettingsApplied` (real translations; `LocalizationTests` must stay green)
- [X] T027 [US1] Create `src/DhcpServer.App/ViewModels/AddressSettingsViewModel.cs`: text fields for address, mask (dotted), gateway, DNS 1/2; `UseFixedAddress` (locked per adapter state), suggestion via `AddressSuggester`, validation via `AddressSettingsValidator` with localized `Field_<Kind>` messages per field, `TryGetSettings(out AddressSettings)`; passes the field/suggestion parts of T021
- [X] T028 [US1] Create `src/DhcpServer.App/ViewModels/IChangeConfirmation.cs` (`bool Confirm(ChangePreview preview, string adapterName)`), `src/DhcpServer.App/ViewModels/ConfirmChangesViewModel.cs` (localized table rows from `ChangePreview`, changed rows flagged) and `src/DhcpServer.App/Views/ConfirmChangesWindow.xaml` (+ minimal `.xaml.cs`, `{l:Loc}` only, auto-sized, default button `Confirm_Apply`, `Button_Cancel`) with a real `WpfChangeConfirmation` in `src/DhcpServer.App/Views/`
- [X] T029 [US1] Extend `src/DhcpServer.App/ViewModels/MainViewModel.cs` and `InterfaceItemViewModel.cs`: configurable adapters become selectable (`IsSelectable` = eligible **or** `IsConfigurable`; `NeedsAddress` flag and `Interface_NeedsAddress` marker), an `AddressSettingsViewModel` property re-initialized on selection change, Start flow: validate → build `ChangePreview` from a fresh `IAdapterConfigService.Read` → `IChangeConfirmation` → `session.StartAsync(chosen, settings)`; no preview and `settings = null` when the adapter serves as it is; group disabled while Starting/Running; passes T021
- [X] T030 [US1] Add the "Address" group (checkbox, fields, optional hints, inline validation text, `AutomationProperties.Name` from the keys) to `src/DhcpServer.App/Views/MainWindow.xaml` per `contracts/ui-contract.md`; auto-sized, no fixed text widths; keep the smoke test `tests/DhcpServer.Tests/ViewModels/MainWindowSmokeTests.cs` green (extend its assertions for the new controls)
- [X] T031 [US1] Wire the real services in `src/DhcpServer.App/App.xaml.cs`: `NetshAdapterConfigService`, `FileRestoreStore`, `NamedMutexSettingsLock`, `AddressConfigurator`, `WpfChangeConfirmation`, passed into `ServerSession` and `MainViewModel`

**Checkpoint**: US1 works end-to-end on a spare adapter (quickstart scenarios 1, 3, 4) — but settings are not yet restored. Dev builds only.

---

## Phase 4: User Story 2 - Get the old settings back (Priority: P1)

**Goal**: Stop, exit and interface loss restore the previous configuration (including automatic mode); after a crash or power loss the next start restores first and only then allows a session.

**Independent Test**: Fake adapter service: after a session the adapter equals its original (static with several addresses, gateway and DNS; and automatic); a record present at startup is restored before Start is possible (quickstart scenarios 1–2, 5–8).

### Tests for User Story 2

- [X] T032 [P] [US2] Add the restore region to `tests/DhcpServer.Tests/Addressing/AddressConfiguratorTests.cs`: restore for previous = automatic (`SetAddressDhcp`, `SetDnsDhcp`), static single, static with several addresses (`SetAddress` then `AddAddress`), with and without gateway, with and without DNS (SC-003); `Verify` after restore; record deleted only after a verified restore; adapter changed by someone else ⇒ nothing executed, record deleted, outcome `LeftUnchanged` (FR-009); adapter missing ⇒ nothing executed, record kept, outcome `AdapterMissing`; record with `restoring=1` skips the "changed" comparison and finishes a half restore; restore twice ends in the same state; a corrupt record ⇒ outcome `RecordCorrupt` and nothing executed
- [X] T033 [P] [US2] Write failing tests `tests/DhcpServer.Tests/Addressing/RecoveryTests.cs`: record present at startup ⇒ `RecoveryService.Run()` restores and reports `Restored`/`LeftUnchanged`; failure or missing adapter ⇒ outcome `Pending` and `CanStartSession == false` until `Retry` succeeds or `Discard` removes the record; corrupt record ⇒ only `Discard` offered; lock held by another instance ⇒ `SettingsLocked` and no change; no record ⇒ `NothingToDo` (SC-004)
- [X] T034 [P] [US2] Add the stop region to `tests/DhcpServer.Tests/Runtime/ServerSessionAddressTests.cs`: `Stop`, `Dispose` and interface loss close the endpoint **before** any restore step (event log); state goes `Running → Stopping → Stopped`; a restore failure still ends in `Stopped` with the record kept and a `RestoreFailed` notice; after `Stop` the adapter equals the original; a session started without settings never touches the service; `Stop` while `Starting` after apply restores as well

### Implementation for User Story 2

- [X] T035 [US2] Implement the restore part of `src/DhcpServer.Core/Addressing/AddressConfigurator.cs`: `RestoreResult Restore()` following `contracts/apply-sequence.md` (steps 1–7, `restoring=1` marker written atomically before the first command, compare with `Applied` per research D6, verify, delete record, release lock) with outcomes `Restored, LeftUnchanged, AdapterMissing, RecordCorrupt, Failed(step, previous settings)`; passes T032
- [X] T036 [US2] Create `src/DhcpServer.Core/Addressing/RecoveryService.cs` (`Run()`, `Retry()`, `Discard()`, `CanStartSession`, state `NothingToDo/Pending/Corrupt/Done`) on top of the configurator; passes T033
- [X] T037 [US2] Extend `src/DhcpServer.Core/Runtime/ServerSession.cs`: `Stop`/`Dispose`/interface-lost close the endpoint first, set `Stopping`, call `Restore()` when this session applied settings, then set `Stopped`; raise a `RestoreOutcome` event (restored / left unchanged / adapter missing / failed with step) for the view model; `StartAsync` refuses while the recovery service reports a pending record (`CanStartSession == false`); passes T034
- [X] T038 [P] [US2] Add US2 string keys to all six `Strings*.resx`: `Status_Stopping`, `Status_Restoring`, `Recovery_Pending` ({0} adapter, {1} previous settings), `Recovery_Retry`, `Recovery_Discard`, `Recovery_DiscardConfirm`, `Recovery_Corrupt`, `Notice_SettingsRestored`, `Notice_SettingsLeftUnchanged`, `Notice_AdapterMissing`
- [X] T039 [US2] Create `src/DhcpServer.App/ViewModels/RecoveryViewModel.cs` and extend `MainViewModel.cs`: run recovery once at construction (before any session can start), banner state with Retry/Discard (Discard asks `Recovery_DiscardConfirm` through a small `IConfirm` abstraction with a fake in tests), Start disabled while pending, `Status_Stopping`/`Status_Restoring` text for `Stopping`, notices for each `RestoreOutcome`; add view-model tests in `tests/DhcpServer.Tests/ViewModels/RecoveryViewModelTests.cs` (banner shown for a record at start, Start disabled until resolved, Retry/Discard flow, notices) and wire `RecoveryService` in `src/DhcpServer.App/App.xaml.cs`
- [X] T040 [US2] Add the recovery banner (text, **Retry**, **Discard**, visibility bound to the view model) and the `Stopping` status handling to `src/DhcpServer.App/Views/MainWindow.xaml`; keep the smoke test green
- [X] T041 [US2] Extend `docs/manual-test-checklist.md` with quickstart scenarios 1–2 (round trip automatic and static), 5–6 (crash, power loss), 7 (changed by someone else), 8 (adapter missing) as a new section "PC address settings (feature 002)", including the `ipconfig /all` observations and the warning to use a spare adapter

**Checkpoint**: US1 + US2 work together — the PC's settings always come back (quickstart 1–2, 5–8).

---

## Phase 5: User Story 3 - Understand failures and never end up half-configured (Priority: P2)

**Goal**: Every failure names its exact step, a failed apply rolls back and never leaves the server running, only the chosen adapter is ever touched, and a second instance cannot interfere.

**Independent Test**: Failure injection at every step of apply and restore with the fake service (quickstart scenarios 9–11).

### Tests for User Story 3

- [X] T042 [P] [US3] Add the failure region to `tests/DhcpServer.Tests/Addressing/AddressConfiguratorTests.cs`: inject a failure at each of `SetAddress`, `SetDns`/`ClearDns`, `Verify` (verify mismatch), `WaitForAddress` ⇒ rollback restores the previous configuration, result names the failing step, record deleted after a successful rollback; failure during rollback ⇒ record kept, both steps reported (FR-010/011); failure at each restore step ⇒ `AddressRestoreFailed(step)` with the previous settings in the result, record kept (SC-005); failure of `Capture` ⇒ nothing changed
- [X] T043 [P] [US3] Add to `tests/DhcpServer.Tests/Runtime/ServerSessionAddressTests.cs`: failed apply ⇒ state `Failed`, endpoint never bound, probe never called after a failed step; failed gate/bind after a successful apply (other DHCP server, port in use) ⇒ rollback restores the old settings and state `Failed` with the gate's reason; two sessions sharing one `FakeSettingsLock` ⇒ the second reports `SettingsLocked` and changes nothing (FR-015); the fake service records that no adapter id other than the chosen one is ever used across apply, rollback and restore (SC-006)
- [X] T044 [P] [US3] Write failing tests `tests/DhcpServer.Tests/ViewModels/AddressFailureMessageTests.cs`: `DetailText` for `AddressApplyFailed` shows the localized step name (`Step_<name>`) and the reason; for `AddressRestoreFailed` also shows the previous settings as readable text; `SettingsLocked`, `RecordNotWritable`, `AddressNotReady` map to their keys; every `ApplyStep` value has a `Step_<name>` key in all languages

### Implementation for User Story 3

- [X] T045 [US3] Add rollback to `src/DhcpServer.Core/Addressing/AddressConfigurator.cs` and `ServerSession.StartAsync`: any failure after the record was written (steps 4–8 of `contracts/apply-sequence.md`, including a failing gate or bind) runs `Restore()` and reports both the original failing step and the rollback result; a failed rollback keeps the record; the final state is `Failed(reason)` and the server never ran; passes T042/T043
- [X] T046 [P] [US3] Add US3 string keys to all six `Strings*.resx`: `Reason_AddressApplyFailed` ({0} step), `Reason_AddressRestoreFailed` ({0} step, {1} previous settings), `Reason_SettingsLocked`, `Reason_RecordNotWritable`, `Reason_AddressNotReady`, `Step_Capture`, `Step_WriteRecord`, `Step_SetAddress`, `Step_AddAddress`, `Step_SetDns`, `Step_ClearDns`, `Step_SetAddressDhcp`, `Step_SetDnsDhcp`, `Step_Verify`, `Step_WaitForAddress`, `Step_DeleteRecord`
- [X] T047 [US3] Extend `src/DhcpServer.App/ViewModels/MainViewModel.cs` so `DetailText` formats the new reasons (localized step name via `Step_<name>`, previous settings text for restore failures); passes T044
- [X] T048 [US3] Extend `docs/manual-test-checklist.md` with quickstart scenarios 9 (failure names the step, old settings back, server not running), 10 (read-only folder ⇒ `Reason_RecordNotWritable`, nothing changed), 11 (second instance ⇒ `Reason_SettingsLocked`)

**Checkpoint**: All three stories complete — Principle II is fully covered and tested.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T049 [P] Update `README.md` (the program can set a fixed address for the session and restores the old settings afterwards; use a spare adapter when trying it; Internet sharing remains out of scope) and add the feature to `docs/release-notes.md` under "Unreleased"
- [ ] T050 [P] (window, confirmation window and banner load in a smoke test; visual clipping check in de/ja still to do by hand) Run the app in German and Japanese and check the new address group, the confirmation window and the recovery banner for clipping; fix widths in `src/DhcpServer.App/Views/MainWindow.xaml` / `ConfirmChangesWindow.xaml` if needed (`LocalizationTests` already guard key parity)
- [X] T051 Run the full gate from CLAUDE.md: `dotnet build DhcpServer.slnx -c Release -warnaserror`, `dotnet test tests/DhcpServer.Tests -c Release --no-build`, `pwsh tests/check-artifact.ps1`, `Invoke-Pester tools/tests`; fix every failure (close any running `DhcpServer.exe` first, it locks the output file)
- [ ] T052 Execute quickstart scenarios 1–12 on a **spare adapter** on Windows 11 and Windows 7 SP1 (incl. the mutating checks left over from T001: does `set address source=static` replace all static addresses, time to `Preferred`), record results and the SHA-256 of the executable in `docs/manual-test-checklist.md`, and update research D2/D4 and `contracts/apply-sequence.md` if reality differs
- [X] T053 Review the diff against `.specify/memory/constitution.md` (especially Principle II: preview before change, record before first change, restore paths, only the chosen adapter, no registry writes/firewall/service changes), confirm no user-visible string is hard-coded and that `netsh` output is never parsed or shown

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup**: T001 and T002 are independent. T001's outcome must be known before US1 starts (it may change the `netsh` commands).
- **Foundational**: T003, T004, T005 are independent; tests T006/T008/T010/T012/T014/T016 can start at once (they need only T003–T005 for types they reference), each followed by its implementation (T007, T009, T011, T013, T015, T017); T018 needs T003, T004, T007, T015. Everything in Phase 2 gates all stories.
- **US1**: needs Phase 2. **US2** needs US1's `AddressConfigurator` and session integration (T022, T023) and the infrastructure (T024, T025). **US3** needs US2's `Restore` (T035), because rollback is a restore.
- **Parallelism between stories**: none; the three stories share `AddressConfigurator.cs`, `ServerSession.cs`, `MainViewModel.cs`, `MainWindow.xaml` and the six resx files, so tasks touching the same file must run sequentially (the `[P]` marks already exclude them).
- **Polish**: after all three stories.

### Within each story

Tests first (they must fail) → Core implementation → App infrastructure → view models → XAML → wiring → manual checklist.

### Parallel opportunities

- Phase 2: T003 ∥ T004 ∥ T005; the six test tasks T006, T008, T010, T012, T014, T016 together; then T007, T009, T011, T013, T015, T017 together.
- US1: T019 ∥ T020 ∥ T021; T024 ∥ T025 ∥ T026.
- US2: T032 ∥ T033 ∥ T034; T038 alongside T035–T037.
- US3: T042 ∥ T043 ∥ T044; T046 alongside T045.
- Polish: T049 ∥ T050.

### Parallel example: Foundational tests

```text
Task: "Write failing tests tests/DhcpServer.Tests/Addressing/AdapterConfigTests.cs"            (T006)
Task: "Write failing tests tests/DhcpServer.Tests/Addressing/AddressSettingsValidatorTests.cs" (T008)
Task: "Write failing tests tests/DhcpServer.Tests/Addressing/AddressSuggesterTests.cs"         (T010)
Task: "Write failing tests tests/DhcpServer.Tests/Addressing/RestoreFileTests.cs"              (T014)
```

## Implementation Strategy

- **Development MVP**: Phases 1–3 (US1) — a fixed address is applied and the server runs. **Dev builds on a spare adapter only; never published.**
- **First releasable increment**: US1 + US2 + US3 (Phases 1–5). The restore and failure paths are what Principle II demands; do US2 straight after US1 and US3 right after, before any manual use on a real machine.
- **Then**: Polish, the manual checklist on a spare adapter on Windows 7 SP1 and 11, update release notes, squash-merge through a pull request with a green `build-test`.
- Commit after each task or logical group on branch `002-pc-ip-settings`.

## Implementation notes (deviations from the text above)

- `IAdapterConfigService.Execute` takes a third parameter `addressIndex` (which of the target's addresses `AddAddress` adds).
- The consent abstraction is `IUserDialogs` (`ConfirmChanges`, `ConfirmDiscard`) in `App/ViewModels/ConfirmChangesViewModel.cs`, not `IChangeConfirmation`; the real implementation is `App/Views/WpfUserDialogs.cs`.
- The cross-instance lock is a **named semaphore** (`NamedSemaphoreSettingsLock`), not a mutex: a mutex must be released by the thread that took it, but applying runs on a worker thread and restoring on another. The OS still drops it when the owning process dies.
- `AdapterConfig.Equivalent` treats two empty DNS lists as equal regardless of mode (the registry shows no static DNS in both cases), otherwise verification after `ClearDns` could never succeed.
- The suggester's `IAddressProber` parameter exists and is tested, but the app passes none: the suggested subnet is usually not yet attached, so ARP/ping on it is meaningless and `SendARP` could block the UI.
- `Status_Stopping` was not added: the `Stopping` state only exists while restoring, so `Status_Restoring` covers it. `Address_Dns` was added for the preview row.
- Tests that touch the static `LanguageService` run in the xUnit collection `UiLanguage` (serialized) to avoid cross-test interference.
- The smoke test sets `ShutdownMode.OnExplicitShutdown`, otherwise closing the first window ends the test application.
