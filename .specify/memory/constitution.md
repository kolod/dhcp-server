<!--
Sync Impact Report
==================
Version change: (template, unversioned) → 1.0.0
Bump rationale: initial ratification; all placeholders replaced with concrete principles.
Principles: I. Network Safety First (NON-NEGOTIABLE), II. Reversible System Changes, III. Portable & Compact,
IV. Windows 7 – Windows 11 Compatibility, V. English-First Engineering / Localized UI, VI. Testable Core,
VII. Simplicity
Templates requiring updates:
- ✅ .specify/templates/plan-template.md — Constitution Check is derived from this file at plan time.
- ✅ .specify/templates/spec-template.md — generic; no mandatory sections added/removed.
- ✅ .specify/templates/tasks-template.md — principle-driven task types (localization, manual
  checklist steps) are generated per feature by /speckit-tasks.
- ✅ .specify/templates/checklist-template.md — generic; no changes.
Deferred TODOs: none.
-->

# DhcpServer Constitution

## Core Principles

### I. Network Safety First (NON-NEGOTIABLE)

- The DHCP server MUST listen and answer only on the single network interface explicitly chosen
  by the user, and MUST NOT start without such a choice.
- Before serving, the application MUST probe the chosen interface for an existing DHCP server and
  MUST refuse to start (with an explanation) if one answers; it MUST also refuse interfaces that
  are down, not Ethernet/Wi-Fi-like, or whose subnet overlaps another active interface.
- Leases MUST be issued only from the configured pool of the chosen interface's subnet; the
  server's own address and the gateway address MUST NEVER be leased, and an address that answers
  ARP/ping while not leased MUST NOT be offered.
- The server MUST stop answering immediately when the interface goes down, is changed, or the
  application exits; it MUST NOT leave any listener running.
- Internet sharing MUST be off by default and enabled only by an explicit per-session user choice.

**Rationale**: a rogue DHCP server on a production or office network breaks every host on it;
the tool is meant for isolated direct links, so it must be impossible to harm any other network.

### II. Reversible System Changes

- Every change to the PC's configuration (adapter IP address, mask, gateway, DNS, Internet
  Connection Sharing) MUST be shown to the user before being applied and MUST require explicit
  confirmation.
- The previous configuration MUST be captured before the change and restored on stop, on exit,
  and — best effort — at the next start after a crash.
- A failure while applying or restoring a change MUST be reported with the exact failing step and
  MUST NOT leave sharing enabled on a different adapter than the one chosen.
- Only the chosen adapter and the sharing relationship the user selected MAY be modified; no
  firewall rules, services, drivers or registry values are created by the application itself.

**Rationale**: a laptop that loses its normal network settings after a diagnostic session is a
worse outcome than no tool at all.

### III. Portable & Compact

- The deliverable MUST be a single executable that runs without installation (copy-and-run from
  any folder or USB stick). Localized UI resources MUST be embedded in that executable.
- The application MUST NOT write to the registry, install services/drivers, or leave files
  outside its own folder and user-chosen paths. Settings MAY be stored in `DhcpServer.ini` next
  to the executable; failing to write it MUST NOT be fatal.
- Third-party runtime dependencies MUST be avoided; any exception MUST be embedded in the
  executable, license-compatible, and justified in the feature plan. Executable size SHOULD stay
  under 5 MB.
- The manifest MUST declare `requireAdministrator` (binding the DHCP port, changing adapter IP settings and enabling Internet Connection Sharing require elevation) and MUST declare DPI
  awareness and Windows 7 – 11 compatibility.

**Rationale**: zero setup on arbitrary machines; nothing to clean up afterwards.

### IV. Windows 7 – Windows 11 Compatibility

- Windows 7 SP1, 8.1, 10 and 11 (x86 and x64) MUST be able to run the same executable.
- Only .NET Framework 4.8 and Win32 APIs available on Windows 7 SP1 MAY be used
  unconditionally. Newer APIs (WinRT, Windows 10+ only Win32 functions) MUST be feature-detected
  with a fallback.
- WPF features MUST work with software rendering (rendering tier 0, e.g. virtual machines and
  Remote Desktop) — no feature may depend on hardware acceleration.
- Any feature that cannot work on Windows 7 MUST be justified in the plan and degrade
  gracefully, never crash.

**Rationale**: the target audience includes legacy and offline machines.

### V. English-First Engineering, Localized UI

- All source code identifiers, code comments, commit messages, specifications, plans, task lists,
  README, and other documentation MUST be written in English, regardless of the language used in
  requests or discussions.
- The UI default language MUST be English. The UI MUST also be fully localized into
  Ukrainian, German, French, Spanish and Japanese.
- No user-visible string MAY be hard-coded in C# or XAML; all UI text, including error messages
  and confirmation dialogs, MUST come from the embedded string tables (`{l:Loc Key}` in XAML,
  `LanguageService` in code). Adding a string requires adding it for all languages.
- The UI language MUST follow the OS UI culture when supported, otherwise English, and MUST be
  switchable at runtime without restart.
- Layouts MUST accommodate longer translations and CJK text without clipping (auto-sized
  `Grid` rows/columns, no fixed widths for text).

**Rationale**: one working language keeps the codebase maintainable; localization reaches users.

### VI. Testable Core

- Domain logic MUST live in `DhcpServer.Core`, which MUST NOT reference WPF or any UI assembly,
  and MUST be unit-tested (xUnit, `net48`). External resources (files, devices, network, clock)
  MUST sit behind small abstractions so tests can use fakes.
- The UI follows MVVM: views contain XAML and only view-specific code-behind; behavior lives in
  view models and the core.
- Every rule of Principles I and II MUST have at least one automated test.
- Before a release, `docs/manual-test-checklist.md` MUST be executed on at least Windows 7 SP1
  and Windows 11 with the exact release executable.

**Rationale**: tests are the only early signal; the manual checklist covers what tests cannot.

### VII. Simplicity

- Scope is limited to: a small IPv4 DHCP server for giving IoT devices addresses over a direct
  (or isolated) link to a laptop, mainly for development and diagnostics — choosing the
  interface, viewing leases and device state, probing common services (web/FTP/SSH) with quick
  launch of a registered program, configuring the PC's own IP address, and optionally sharing the
  PC's Internet connection with the devices. Features outside that scope (DHCPv6, DHCP relay,
  multiple simultaneous scopes/interfaces, a Windows service or background daemon, a general
  network scanner, a DNS server, VPN/firewall management, remote administration) require a
  constitution amendment.
- Prefer the simplest design that satisfies the principles above (YAGNI); abstractions, MVVM
  frameworks, DI containers and plugins MUST be justified by a concrete current need.

**Rationale**: a compact tool stays reliable by staying small.

## Technology & Platform Constraints

- **Language**: C# 7.3.
- **Runtime**: .NET Framework 4.8 (ships with Windows 10 1903+ and Windows 11, installable on
  Windows 7 SP1 / 8.1). .NET Core/5+ MUST NOT be used: it does not support Windows 7 without a
  large self-contained runtime that violates Principle III.
- **UI**: WPF (SDK-style project, `UseWPF`), MVVM without a framework.
- **Libraries**: BCL only by default; see Principle III for exceptions.
- **Native interop**: P/Invoke to Win32 is allowed within Principle IV; no kernel drivers.
- **Localization**: `.resx` string tables compiled into the main assembly with
  `WithCulture="false"` (no satellite folders in the shipped artifact).
- **Build**: SDK-style projects targeting `net48`, `AnyCPU` (prefer 32-bit off), one executable;
  the core is compiled into the executable as linked sources. Reproducible build via
  `dotnet build` (no Visual Studio required).
- **Testing**: xUnit test project targeting `net48`; Pester 5 for PowerShell tooling.
- **CI/CD**: GitHub Actions on Windows runners; actions pinned by commit SHA; releases are drafts
  published manually after the checklist.
- **Protocols**: DHCPv4 per RFC 2131/2132 over UDP 67/68 using plain sockets; ARP/ICMP probing
  via Win32 `iphlpapi`; service probes are plain TCP connects (no credentials ever sent).
- **System changes**: adapter IP configuration and Internet Connection Sharing via APIs present on
  Windows 7 SP1 (WMI/`netsh`/the `HNetCfg` COM API); Windows 10+ only alternatives MUST be
  feature-detected (Principle IV).
- **Registered programs**: quick-launch entries are user-defined executable + arguments stored in
  `DhcpServer.ini`; they MUST be started without a shell and only on explicit user action.

## Development Workflow & Quality Gates

- Work follows Spec Kit: specify → (clarify) → plan → tasks → implement. Each plan MUST pass the
  Constitution Check against all principles before research and again after design.
- A change is mergeable only when: it builds with warnings as errors, all automated tests pass
  (including localization tests), `tests/check-artifact.ps1` passes, no hard-coded UI strings were
  introduced, all languages have entries for new strings, and all code/docs are in English.
- Changes go to `main` only through squash-merged pull requests with a green `build-test` check.
- Releases MUST record the SHA-256 hash of the shipped executable and the manual checklist
  results.

## Governance

- This constitution supersedes all other development practices for this project. Where a
  spec, plan, or task conflicts with it, the constitution wins until amended.
- Amendments require: a written proposal (in English) describing the change and its rationale,
  an updated Sync Impact Report at the top of this file, and propagation to dependent
  templates under `.specify/templates/`.
- Versioning follows semantic versioning: MAJOR for removing or redefining a principle, MINOR
  for adding a principle/section or materially expanding guidance, PATCH for clarifications.
- Compliance is reviewed at every plan's Constitution Check and in every code review; any
  justified deviation MUST be recorded in the plan's Complexity Tracking table.

**Version**: 1.0.0 | **Ratified**: 2026-11-05 | **Last Amended**: 2026-11-05
