---

description: "Task list for the DHCP Lease Server feature"
---

# Tasks: DHCP Lease Server

**Input**: Design documents from `/specs/001-dhcp-lease-server/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (dhcp-behavior.md, lease-file.md, ui-contract.md), quickstart.md

**Tests**: INCLUDED. The constitution (Principle VI) requires an automated test for every rule of Principles I and II, and the core must be unit-tested. Test tasks come before the implementation they cover; write them first and see them fail.

**Organization**: Tasks are grouped by user story. Stories US1–US3 are all P1: US1 delivers a working server, US2 the lease list, US3 the safety gate. **Nothing may be released before US3 is done** (Principle I); do US3 immediately after US1/US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: US1–US4 from spec.md
- Paths are relative to the repository root. `Core` = `src/DhcpServer.Core`, `App` = `src/DhcpServer.App`, `Tests` = `tests/DhcpServer.Tests`
- Language rules for every task: English only in code/comments/docs; C# 7.3; no user-visible text in code or XAML; any new string key goes into **all six** `App/Resources/Strings*.resx` (en, uk, de, fr, es, ja) with a real translation; build must pass `-warnaserror`.

## Phase 1: Setup

**Purpose**: Make the test project able to reach the view models, and de-risk the two platform assumptions before building on them.

- [X] T001 Add `<InternalsVisibleTo Include="DhcpServer.Tests" />` to `src/DhcpServer.App/DhcpServer.App.csproj` and a `ProjectReference` to `..\..\src\DhcpServer.App\DhcpServer.App.csproj` (plus `Reference Include="PresentationFramework;PresentationCore;WindowsBase;System.Xaml"`) in `tests/DhcpServer.Tests/DhcpServer.Tests.csproj`, so view-model tests can run; keep `dotnet build DhcpServer.slnx -c Release -warnaserror` and `dotnet test` green and confirm no extra file lands next to the shipped exe
- [X] T002 (Windows 11 part done; Windows 7 SP1 part moves to T054) Spike (not shipped): in the session scratchpad write a throw-away console program that, on Windows 11 and Windows 7 SP1, (a) binds a UDP socket to `<interface IPv4>:67` with `ExclusiveAddressUse` and verifies it receives a limited-broadcast DISCOVER from another host and nothing from a second interface (research D1), (b) binds `<interface IPv4>:68` with `ReuseAddress` while the Windows DHCP Client service runs and sends/receives a probe DISCOVER (research D3), (c) checks `SendARP` from the same interface. Record outcomes (OS, result, any workaround) in a new "Spike results" section at the end of `specs/001-dhcp-lease-server/research.md`; if (b) fails, stop and amend plan.md per research D3 before starting US3

**Checkpoint**: Test project compiles against App; spike results recorded.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions and pure domain types used by every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create the abstractions in `src/DhcpServer.Core/Abstractions/`: `IClock.cs` (`DateTime UtcNow`), `IInterfaceProvider.cs` (`IReadOnlyList<InterfaceInfo> GetInterfaces()`, event `Changed`), `IDhcpEndpoint.cs` (`bool TryReceive(out byte[] data)` blocking until data or closed, `void SendBroadcast(byte[] data)`, `void Close()`), `IEndpointFactory.cs` (`IDhcpEndpoint Bind(IPAddress localAddress)` throwing `EndpointBindException` with a `FailureReason`), `IAddressProber.cs` (`bool IsInUse(IPAddress address, IPAddress viaInterface)`), `IDhcpServerProbe.cs` (`ProbeResult Probe(IPAddress viaInterface)` → `NoServer`, `ServerFound(IPAddress)`, `Inconclusive`), `ILeaseStore.cs` (`IReadOnlyList<Lease> Load()`, `void Save(IReadOnlyList<Lease>)`), per data-model.md
- [X] T004 [P] Create `src/DhcpServer.Core/Runtime/FailureReason.cs` (immutable: `Kind` enum `NoInterfaceChosen, InterfaceDown, InterfaceUnsupported, NoIpv4Address, LinkLocalAddress, SubnetTooSmall, SubnetOverlap, OtherDhcpServer, ProbeInconclusive, PortInUse, AccessDenied, InterfaceLost, UnexpectedError` + optional `Argument` string + `ResourceKey` property returning `Reason_<Kind>`) and `src/DhcpServer.Core/Runtime/ServerState.cs` (`Stopped, Starting, Running, Failed`)
- [X] T005 [P] Create `src/DhcpServer.Core/Network/IpMath.cs` (mask from prefix, network, broadcast, `IsInSubnet`, `ToUInt32`/`FromUInt32`, subnet overlap test, link-local test) and tests in `tests/DhcpServer.Tests/Network/IpMathTests.cs` covering /24, /30, /16, edge prefixes and overlap cases
- [X] T006 [P] Write failing tests `tests/DhcpServer.Tests/Network/InterfaceEligibilityTests.cs`: one test per reason in research D5 (Down, NotSupportedType, NoIpv4Address, LinkLocalAddress, SubnetTooSmall, SubnetOverlap) incl. rule order, plus an eligible Ethernet and Wi-Fi case and "overlap with a Down interface is ignored" (FR-003, Principle I)
- [X] T007 Implement `src/DhcpServer.Core/Network/InterfaceInfo.cs` and `src/DhcpServer.Core/Network/InterfaceEligibility.cs` (`Evaluate(interface, allInterfaces)` → `Eligibility` with `IsEligible` and `FailureReason`) to make T006 pass
- [X] T008 [P] Write failing tests `tests/DhcpServer.Tests/Dhcp/DhcpMessageTests.cs`: round-trip of DISCOVER/OFFER/REQUEST/ACK/NAK; options 1,3,12,28,50,51,53,54,55,58,59,61; rejection of length < 240, bad magic cookie, `op != 1` on input, `hlen != 6`, `htype != 1`, missing option 53, truncated option; fuzz-style loop of random byte arrays must never throw (FR-017)
- [X] T009 Implement `src/DhcpServer.Core/Dhcp/DhcpOptions.cs` and `src/DhcpServer.Core/Dhcp/DhcpMessage.cs` (`TryParse(byte[], out DhcpMessage)`, `ToBytes()`, option helpers, message-type enum) to make T008 pass
- [X] T010 [P] Write failing tests `tests/DhcpServer.Tests/Leases/AddressPoolTests.cs`: /24 gives .1-based exclusions correct, never contains network/broadcast/server/gateway (Principle I), gateway outside subnet ignored, /30 gives 1 usable, /16 capped to 254-address window, deterministic order, `Contains`
- [X] T011 Implement `src/DhcpServer.Core/Leases/AddressPool.cs` per data-model.md and research D6 to make T010 pass
- [X] T012 [P] Write failing tests `tests/DhcpServer.Tests/Leases/LeaseTableTests.cs`: same MAC gets same address (FR-007); requested IP honored when free; lowest free otherwise; offers reserve 60 s then lapse; no two MACs on one address; expiry marks Expired; release marks Released and returns address to reuse; reuse order (never-used first, then least recently used); pool exhaustion result (FR-016); `Snapshot` ordering by IP; `Changed` raised once per mutation; `isInUse` callback skips addresses and is bypassed for the device's own valid lease
- [X] T013 Implement `src/DhcpServer.Core/Leases/Lease.cs` (fields and state per data-model.md) and `src/DhcpServer.Core/Leases/LeaseTable.cs` (constants: lease 14 400 s, T1/T2, offer hold 60 s) to make T012 pass
- [X] T014 [P] Create test fakes in `tests/DhcpServer.Tests/Fakes/`: `FakeClock.cs` (settable, `Advance`), `FakeEndpoint.cs` + `FakeEndpointFactory.cs` (records bound address and sent packets, `Enqueue` input, can throw `EndpointBindException`), `FakeInterfaceProvider.cs` (mutable list, raises `Changed`), `FakeAddressProber.cs`, `FakeDhcpServerProbe.cs`, `InMemoryLeaseStore.cs`, `TestPackets.cs` (builders for DISCOVER/REQUEST/RELEASE/DECLINE/INFORM)

**Checkpoint**: Foundation ready — all Core primitives built and tested; stories can start.

---

## Phase 3: User Story 1 - Start serving addresses on a chosen interface (Priority: P1) 🎯 MVP

**Goal**: The user picks an interface, presses Start, and a DHCP client on that interface receives an address; Stop (or exit) stops answering at once.

**Independent Test**: With a fake endpoint, a DISCOVER→OFFER→REQUEST→ACK exchange yields a pool address; manually, a real device on the cable gets an address (quickstart scenario 1). Server never starts without a chosen interface or automatically.

### Tests for User Story 1

- [X] T015 [P] [US1] Write failing tests `tests/DhcpServer.Tests/Dhcp/DhcpEngineTests.cs` against `contracts/dhcp-behavior.md`: full exchange, offer options (1,3,28,51,53,54,58,59 with correct values), renewal via unicast REQUEST with `ciaddr`, init-reboot ACK and NAK cases, REQUEST for another server id ignored, DECLINE, RELEASE, INFORM, requests for server/gateway address → NAK, `giaddr != 0` ignored, malformed input ignored, returning MAC gets same address after restart of the engine with the same table (FR-007/008/017)
- [X] T016 [P] [US1] Write failing tests `tests/DhcpServer.Tests/Runtime/ServerSessionTests.cs` (basic part): Start without a chosen interface → `Failed(NoInterfaceChosen)` and nothing bound (FR-001/018); Start binds exactly the chosen interface address and no other (FR-002); packets are answered through the same endpoint; Stop closes the endpoint and state becomes `Stopped` (FR-014); exit/Dispose closes the endpoint; bind error `PortInUse`/`AccessDenied` → `Failed` with that reason; session never auto-starts on construction

### Implementation for User Story 1

- [X] T017 [US1] Implement `src/DhcpServer.Core/Dhcp/DhcpEngine.cs`: `byte[] Handle(byte[] datagram, DateTime now)` returning the reply bytes or null; uses `DhcpMessage`, `AddressPool`, `LeaseTable`; raises `PoolExhausted`; exactly the behavior in contracts/dhcp-behavior.md (without address probing, added in US3); passes T015
- [X] T018 [US1] Implement `src/DhcpServer.Core/Runtime/ServerSession.cs`: constructor takes `IEndpointFactory`, `IClock`, `LeaseTable`; `Start(InterfaceInfo chosen)` (checks chosen non-null and eligible via `InterfaceEligibility`, builds pool, `Bind`, starts a dedicated receive thread, sets state) and `Stop()`/`Dispose()` (closes endpoint first, joins thread, then updates state); events `StateChanged`, `PoolExhausted`; engine guarded by a lock; receive loop swallows per-packet exceptions (FR-017); passes T016
- [X] T019 [P] [US1] Implement `src/DhcpServer.App/Infrastructure/SystemClock.cs` and `src/DhcpServer.App/Infrastructure/UdpEndpointFactory.cs` (UDP socket bound to `<addr>:67`, `ExclusiveAddressUse`, `EnableBroadcast`, `SendBroadcast` to `255.255.255.255:68`, `Close` unblocks the receive thread, `SocketException` mapped to `EndpointBindException` with `PortInUse`/`AccessDenied`)
- [X] T020 [P] [US1] Implement `src/DhcpServer.App/Infrastructure/NetworkInterfaceProvider.cs`: maps `System.Net.NetworkInformation` adapters to `InterfaceInfo` (id, name, kind, status, first non-link-local unicast IPv4, prefix length, gateway, MAC) and raises `Changed` on `NetworkChange.NetworkAddressChanged`/`NetworkAvailabilityChanged`
- [X] T021 [P] [US1] Extend `src/DhcpServer.App/Settings/PortableSettings.cs` with `LastInterface` (string, key `LastInterface` in `DhcpServer.ini`) and add a test in `tests/DhcpServer.Tests/Settings/IniDocumentTests.cs` style if logic lives in Core
- [X] T022 [P] [US1] Add US1 string keys to all six `src/DhcpServer.App/Resources/Strings*.resx`: `Interface_Label`, `Interface_NoAddress`, `Interface_Status_Up`, `Interface_Status_Down`, `Button_Start`, `Button_Stop`, `Button_Cancel`, `Hint_ChooseInterface`, `Status_Stopped`, `Status_Starting`, `Status_Running`, `Status_Failed`, `Notice_PoolExhausted`, `Reason_NoInterfaceChosen`, `Reason_PortInUse`, `Reason_AccessDenied`, `Reason_Unexpected` (real translations; `LocalizationTests` must stay green)
- [X] T023 [US1] Create `src/DhcpServer.App/ViewModels/InterfaceItemViewModel.cs` and extend `src/DhcpServer.App/ViewModels/MainViewModel.cs`: interface list, `SelectedInterface`, `StartStopCommand` (disabled without selection, shows Cancel while Starting), state/status text via `LanguageService.Format`, reason text from `FailureReason.ResourceKey`; takes `ServerSession`, `IInterfaceProvider`, `PortableSettings`, a UI `SynchronizationContext`; constructed in `src/DhcpServer.App/App.xaml.cs` with the real infrastructure; the session is disposed on `Application.Exit`
- [X] T024 [US1] Extend `src/DhcpServer.App/Views/MainWindow.xaml` (and minimal `.xaml.cs`) per `contracts/ui-contract.md`: interface ComboBox, Start/Stop button, status area with wrapping reason text; all text via `{l:Loc Key}`; auto-sized Grid rows/columns; automation names from the same keys
- [X] T025 [P] [US1] Write view-model tests `tests/DhcpServer.Tests/ViewModels/MainViewModelStartStopTests.cs` (using the fakes and a synchronous `SynchronizationContext`): button disabled without selection, status text per state, Start/Stop flow, never starts on construction (FR-015/018)

**Checkpoint**: US1 works end-to-end in dev builds (quickstart scenario 1). Not releasable yet.

---

## Phase 4: User Story 2 - See the list of leases (Priority: P1)

**Goal**: A live, sorted list of leases with IP, MAC, host name, start, expiry and state; persists across restarts; copy support.

**Independent Test**: Fake clients obtain addresses and rows appear/update/change state; reopen the app and rows come back as inactive (quickstart scenarios 2–3).

### Tests for User Story 2

- [X] T026 [P] [US2] Write failing tests `tests/DhcpServer.Tests/Leases/LeaseFileTests.cs` against `contracts/lease-file.md`: round-trip, header check (wrong header ⇒ ignored), corrupt/short lines skipped, duplicate mac/ip resolution, host name sanitizing and 63-char cap, 1 024-entry cap dropping oldest inactive first, `\r\n`/`\n` input
- [X] T027 [P] [US2] Write failing tests `tests/DhcpServer.Tests/ViewModels/LeaseListTests.cs`: new lease adds a row within the same update, renewal updates in place without duplicate or selection loss, release/expiry changes the state text, all rows show `Inactive` while the server is stopped, ordering by IP numerically (10.0.0.2 before 10.0.0.10), copy commands produce `IP<TAB>MAC` / IP / MAC (FR-010/011/012/013)
- [X] T028 [P] [US2] Write failing persistence test in `tests/DhcpServer.Tests/Runtime/ServerSessionTests.cs` (new `LeasePersistence` region): leases are saved after a change (throttled to ≤ 1 per second via `FakeClock`), flushed on Stop, loaded at construction, and a store that throws on Load/Save never breaks the session (raises a `StoreFailed` notice only)

### Implementation for User Story 2

- [X] T029 [US2] Implement `src/DhcpServer.Core/Leases/LeaseFile.cs` (static `Parse(string)` / `Serialize(IEnumerable<Lease>)` per the contract) to pass T026
- [X] T030 [P] [US2] Implement `src/DhcpServer.App/Infrastructure/FileLeaseStore.cs`: `ILeaseStore` over `DhcpServer.leases` next to the executable, write to `.tmp` then replace, delete temp on failure, never throws (returns/raises failure instead)
- [X] T031 [US2] Wire persistence into `src/DhcpServer.Core/Runtime/ServerSession.cs` (load on construction into the `LeaseTable`, throttled save on `LeaseTable.Changed`, flush on Stop/Dispose, `StoreFailed` event) to pass T028; register `FileLeaseStore` in `src/DhcpServer.App/App.xaml.cs`
- [X] T032 [P] [US2] Add US2 string keys to all six `Strings*.resx`: `Leases_Header`, `Leases_Empty`, `Column_IpAddress`, `Column_MacAddress`, `Column_HostName`, `Column_Start`, `Column_Expires`, `Column_State`, `LeaseState_Active`, `LeaseState_Expired`, `LeaseState_Released`, `LeaseState_Inactive`, `Menu_CopyIp`, `Menu_CopyMac`, `Notice_LeaseFileNotWritable`
- [X] T033 [US2] Create `src/DhcpServer.App/ViewModels/LeaseItemViewModel.cs` and extend `MainViewModel.cs` with an `ObservableCollection<LeaseItemViewModel>` that is reconciled in place from `LeaseTable.Snapshot` on `Changed` (marshalled to the UI thread), a 1-second timer that refreshes state text/expiry colouring while visible, and `CopyIpCommand`/`CopyMacCommand`/`CopyRowCommand`; passes T027
- [X] T034 [US2] Add the lease `DataGrid` (columns per ui-contract.md, dimmed inactive rows, empty-state text, `Ctrl+C` binding, context menu with *Copy IP address* / *Copy MAC address*, local short date/time format) to `src/DhcpServer.App/Views/MainWindow.xaml`; surface `Notice_LeaseFileNotWritable` and `Notice_PoolExhausted` in the status area

**Checkpoint**: US1 + US2 work independently and together (quickstart scenarios 1–3).

---

## Phase 5: User Story 3 - Refuse unsafe situations (Priority: P1)

**Goal**: The server never starts on an unsafe interface, never offers an address already in use, and stops within 2 s when the interface is lost. Satisfies Principle I.

**Independent Test**: Fakes simulate another DHCP server, down interface, overlapping subnet, inconclusive probe, answering address and vanishing interface; each yields refusal/stop and no packet sent (quickstart scenarios 4–7).

### Tests for User Story 3

- [X] T035 [P] [US3] Write failing tests in `tests/DhcpServer.Tests/Runtime/ServerSessionTests.cs` (new `Safety` region): OtherDhcpServer from the probe ⇒ `Failed(OtherDhcpServer)` and the endpoint is never bound; `Inconclusive` probe ⇒ `Failed(ProbeInconclusive)` and nothing bound; each eligibility reason ⇒ matching `Failed` and nothing bound; probe is invoked before bind (call order); cancellation while Starting returns to `Stopped` without binding
- [X] T036 [P] [US3] Write failing tests `tests/DhcpServer.Tests/Runtime/UsedAddressTests.cs`: an address for which `FakeAddressProber` answers is not offered and the next free one is; marked in-use for 10 min via `FakeClock`; a device with its own valid lease skips probing (FR-006, D4)
- [X] T037 [P] [US3] Write failing tests `tests/DhcpServer.Tests/Runtime/InterfaceLossTests.cs`: provider event or 2-second poll detecting interface removed / not Up / address changed ⇒ endpoint closed first, then state `Stopped` with `InterfaceLost` reason; no reply is sent after loss; subnet overlap appearing while running also stops the server
- [X] T038 [P] [US3] Write failing isolation test in `tests/DhcpServer.Tests/Runtime/ServerSessionTests.cs`: with two interfaces known to the provider, only the chosen one's address is ever passed to `IEndpointFactory.Bind`, and changing the selection while running is rejected until Stop (FR-002, SC-005)

### Implementation for User Story 3

- [X] T039 [US3] Implement `src/DhcpServer.Core/Runtime/StartGate.cs` (ordered checks: chosen → eligibility → probe → bind result mapping; returns `FailureReason` or success) and use it from `ServerSession.Start` (state `Starting` during probe, cancellable, runs on a worker thread, never blocks the UI); passes T035/T038
- [X] T040 [US3] Add address probing to `LeaseTable`/`DhcpEngine` via `IAddressProber` with the 10-minute in-use memory (`src/DhcpServer.Core/Leases/LeaseTable.cs`, `src/DhcpServer.Core/Dhcp/DhcpEngine.cs`); passes T036
- [X] T041 [US3] Implement interface-loss monitoring in `src/DhcpServer.Core/Runtime/ServerSession.cs` using `IInterfaceProvider.Changed` plus a 2-second poll timer (injectable for tests); stop order = close endpoint, then state; passes T037
- [X] T042 [P] [US3] Implement `src/DhcpServer.App/Infrastructure/DhcpServerProbe.cs`: sends the probe DISCOVER from `<addr>:68` with a random `02:xx:xx:xx:xx:xx` MAC, waits 3 s twice for an OFFER (any source), returns `NoServer` / `ServerFound` / `Inconclusive` on bind failure per `contracts/dhcp-behavior.md`; honors cancellation
- [X] T043 [P] [US3] Implement `src/DhcpServer.App/Infrastructure/ArpIcmpProber.cs`: `SendARP` P/Invoke (`iphlpapi.dll`, source = chosen interface address) with a 300 ms ICMP echo fallback via `System.Net.NetworkInformation.Ping`; no exceptions escape (returns "not in use" on API failure and logs nothing user-visible)
- [X] T044 [P] [US3] Add US3 string keys to all six `Strings*.resx`: `Reason_Down`, `Reason_NotSupportedType`, `Reason_NoIpv4Address`, `Reason_LinkLocalAddress`, `Reason_SubnetTooSmall`, `Reason_SubnetOverlap`, `Reason_OtherDhcpServer` (with `{0}` address), `Reason_ProbeInconclusive`, `Reason_InterfaceLost`, `Notice_FirewallHint`
- [X] T045 [US3] Register the probe and prober in `src/DhcpServer.App/App.xaml.cs`; in `MainViewModel.cs` show the refusal reason in the status area, enable Cancel while `Starting`, show `Notice_FirewallHint` when Running but no request has arrived for 60 s, and notify the user when the session stops because of interface loss
- [X] T046 [US3] Extend `docs/manual-test-checklist.md` with quickstart scenarios 4–8 (wrong interface, cable pull, overlap/link-local, isolation, exit) including the expected `netstat -ano -p udp` observation

**Checkpoint**: US1–US3 complete — the build is releasable with respect to Principle I.

---

## Phase 6: User Story 4 - Choose the interface conveniently (Priority: P2)

**Goal**: Interface list shows name, IPv4 and status; unusable adapters are greyed with a reason; last choice is remembered but never auto-started.

**Independent Test**: Provider fakes with wired, Wi-Fi, virtual, down and no-address adapters render correctly and unusable ones cannot be selected (quickstart scenario 6).

### Tests for User Story 4

- [X] T047 [P] [US4] Write failing tests `tests/DhcpServer.Tests/ViewModels/InterfaceListTests.cs`: every adapter listed with `name — IPv4 — status`; ineligible ones flagged with their reason key and `IsSelectable == false`; list refreshes on provider `Changed` while stopped and does not change the selection while running; last interface id restored from `PortableSettings` when it still exists and the server remains `Stopped` (FR-003, US4-3, FR-018)

### Implementation for User Story 4

- [X] T048 [US4] Extend `src/DhcpServer.App/ViewModels/InterfaceItemViewModel.cs` and `MainViewModel.cs`: `IsSelectable`, `UnavailableReason` text, display string, refresh on `IInterfaceProvider.Changed`, restore and save `LastInterface`; passes T047
- [X] T049 [US4] Update the interface `ComboBox` in `src/DhcpServer.App/Views/MainWindow.xaml`: item template with greyed unavailable items, `IsEnabled` bound to `IsSelectable`, tooltip with the reason, disabled while the server is running or starting

**Checkpoint**: All four stories work independently.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T050 [P] (LocalizationTests pass and the window loads in a smoke test; visual clipping check in de/ja still to do by hand) Confirm `LocalizationTests` pass with all new keys and that layouts in German and Japanese do not clip (run the app in each language; fix XAML widths if needed) in `src/DhcpServer.App/Views/MainWindow.xaml`
- [X] T051 [P] Update `README.md` (what the tool does, how to use interface/Start/lease list, firewall prompt note, the Internet-sharing and IP-configuration features being out of scope) and add the feature entry to `docs/release-notes.md`
- [X] T052 [P] Extend `tests/check-artifact.ps1` only if needed (still one exe, no added DLLs, size < 5 MB, manifest `requireAdministrator`) and run it
- [X] T053 Run the full gate from CLAUDE.md: `dotnet build DhcpServer.slnx -c Release -warnaserror`, `dotnet test tests/DhcpServer.Tests -c Release --no-build`, `pwsh tests/check-artifact.ps1`; fix every failure
- [ ] T054 Execute manual scenarios 1–10 from `specs/001-dhcp-lease-server/quickstart.md` on Windows 11 and on Windows 7 SP1 (including software rendering) and record results and the executable's SHA-256 in `docs/manual-test-checklist.md`; also confirm research D12 (router option on a real device) and update `research.md` if the router option must change
- [X] T055 Review the diff against `.specify/memory/constitution.md` (Principles I–VII), confirm no user-visible string is hard-coded, and that no listener remains after Stop/exit (`netstat -ano -p udp`)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: T001 first; T002 can run in parallel with T001 but must finish (or its D3 outcome be known) before US3 starts.
- **Foundational (Phase 2)**: needs T001. Inside it: T003/T004/T005 are independent; T006→T007 (needs T005); T008→T009; T010→T011 (needs T005); T012→T013 (needs T011); T014 needs T003, T004, T009. T007, T009, T011, T013, T014 gate all stories.
- **US1 (Phase 3)**: needs Phase 2. **US2** needs US1's session and view model (T018, T023). **US3** needs T018 (`ServerSession`) and T017; it can run in parallel with US2 apart from shared files (`ServerSession.cs`, `MainViewModel.cs`, `MainWindow.xaml`, resx files — serialize edits to those). **US4** needs T023 and T007.
- **Polish**: after all desired stories.

### Within each story

Tests first (they must fail) → Core implementation → App infrastructure → view model → XAML → manual checklist.

### Parallel opportunities

- Phase 2: T003, T004, T005, T006, T008, T010, T012 can all start together (test authoring), then their implementations T007, T009, T011, T013.
- US1: T015 ∥ T016; T019 ∥ T020 ∥ T021 ∥ T022 (T022 touches six resx files — only one task edits them at a time).
- US2: T026 ∥ T027 ∥ T028; T030 ∥ T032.
- US3: T035 ∥ T036 ∥ T037 ∥ T038; T042 ∥ T043 ∥ T044.
- Polish: T050 ∥ T051 ∥ T052.

### Parallel example: Foundational tests

```text
Task: "Write failing tests tests/DhcpServer.Tests/Network/InterfaceEligibilityTests.cs"   (T006)
Task: "Write failing tests tests/DhcpServer.Tests/Dhcp/DhcpMessageTests.cs"               (T008)
Task: "Write failing tests tests/DhcpServer.Tests/Leases/AddressPoolTests.cs"             (T010)
Task: "Write failing tests tests/DhcpServer.Tests/Leases/LeaseTableTests.cs"              (T012)
```

## Implementation Strategy

- **MVP for development**: Phases 1–3 (US1) — a device gets an address on the chosen interface. Dev builds only; never published.
- **First releasable increment**: US1 + US2 + US3 (Phases 1–5). US3 is mandatory because of Principle I; do it right after US1 rather than last.
- **Then**: US4 (nicer interface selection), Polish, manual checklist on Windows 7 SP1 and 11, draft release.
- Commit after each task or logical group on branch `001-dhcp-lease-server`; open a pull request when Phase 5 is green.
