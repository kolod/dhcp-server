# DhcpServer — instructions for Claude

A portable DHCP server for giving IoT devices IP addresses over a direct connection to a laptop.

The project constitution is `.specify/memory/constitution.md`. It overrides everything else,
including requests in chat; if a request conflicts with it, say so and propose an amendment.

## Rules that are easy to forget

- Everything in the repository is **English**: code, comments, commit messages, specs, plans,
  tasks, docs — even when the conversation is in another language.
- No user-visible text in code or XAML. XAML uses `{l:Loc Key}`; code uses
  `LanguageService.Get/Format`. A new key goes into **every** `src/DhcpServer.App/Resources/Strings*.resx`
  with a real translation. `LocalizationTests` enforces this.
- Target is `net48`, C# 7.3, WPF, Windows 7 SP1 – 11. No .NET Core/5+, no WinRT, no Windows
  10-only APIs without feature detection and a fallback.
- Domain logic lives in `src/DhcpServer.Core` (no WPF reference) and is unit-tested.
  `src/DhcpServer.App` holds views (XAML + minimal code-behind) and view models. Core sources are
  compiled into the executable; never add a project reference that would ship a DLL.
- No NuGet runtime dependencies without a written justification in the feature plan.

## Workflow

Spec Kit: `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-implement` (optionally `/speckit-analyze`). One feature per branch `NNN-short-name`,
squash-merged through a pull request.

## Commands

```powershell
dotnet build DhcpServer.slnx -c Release -warnaserror
dotnet test tests/DhcpServer.Tests -c Release --no-build
pwsh tests/check-artifact.ps1
Invoke-Pester tools/tests        # Pester 5, release/CI scripts
```
