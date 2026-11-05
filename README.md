# DHCP Server

[![CI](https://github.com/kolod/dhcp-server/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/kolod/dhcp-server/actions/workflows/ci.yml)

A portable DHCP server for giving IoT devices IP addresses over a direct connection to a laptop.

- Gives IPv4 addresses to IoT devices connected directly (cable or isolated switch) to the PC, on one
  interface you choose, and shows a live list of leases (IP, MAC, host name, expiry).
- Safe by design: refuses to start if another DHCP server answers on the interface, never answers on
  any other interface, and stops at once when the interface goes down or the program exits.
- Portable: a single executable, no installation; settings are kept in `DhcpServer.ini` and leases in
  `DhcpServer.leases` next to it.
- UI languages: English, Ukrainian, German, French, Spanish, Japanese.

## Requirements

- Windows 7 SP1, 8.1, 10 or 11 (x86 or x64).
- .NET Framework 4.8 (preinstalled on Windows 10 1903+ and Windows 11; on Windows 7 SP1 and
  8.1 it must be installed once).

## Download

Get `DhcpServer.exe` from the [releases](https://github.com/kolod/dhcp-server/releases) and check it
against `SHA256SUMS`:

```powershell
Get-FileHash .\DhcpServer.exe
```

## Building

Requires the .NET SDK from `global.json` (Windows). No Visual Studio needed.

```powershell
dotnet build DhcpServer.slnx -c Release
dotnet test tests/DhcpServer.Tests -c Release --no-build
pwsh tests/check-artifact.ps1
```

The executable is `src/DhcpServer.App/bin/Release/net48/DhcpServer.exe`.

## Development

Features are developed with [Spec Kit](https://github.com/github/spec-kit): `specs/NNN-name/`
holds the specification, plan and tasks of each feature. The rules every change must follow are
in [`.specify/memory/constitution.md`](.specify/memory/constitution.md). Releases:
[docs/releasing.md](docs/releasing.md).

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE).
