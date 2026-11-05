# Data Model: PC Address Settings

All types in `DhcpServer.Core` unless marked *(App)*. Immutable classes, C# 7.3.

## AddressSettings (what the user wants)

| Field | Meaning |
|-------|---------|
| `Address` | IPv4 address |
| `PrefixLength` | 8–30 (entered as dotted mask in the UI) |
| `Gateway` | optional IPv4 |
| `DnsServers` | 0–2 IPv4 addresses |

Validation (`AddressSettingsValidator.Validate(settings, chosenInterfaceId, allInterfaces)` →
list of `FieldError { Field, Kind }`): rules of research D9. `Field` ∈ `Address, Mask, Gateway, Dns1,
Dns2`; `Kind` ∈ `Required, Malformed, NotUnicast, LinkLocal, MaskInvalid, MaskSize, NetworkOrBroadcast,
GatewayOutsideSubnet, DnsDuplicate, SubnetOverlap`. Each maps to a string key `Field_<Kind>`.

## AdapterConfig (a snapshot of one adapter's IPv4 configuration)

| Field | Meaning |
|-------|---------|
| `AdapterId` | `NetworkInterface.Id` (GUID string) |
| `InterfaceIndex` | numeric index at snapshot time |
| `Addresses` | list of (address, prefix) — every IPv4 address, in order |
| `Gateway` | default gateway or none |
| `DnsServers` | static DNS list (from the registry `NameServer`), empty when automatic |
| `AddressMode` | `Automatic` (DHCP) or `Static` |
| `DnsMode` | `Automatic` or `Static` |

`Equivalent(a, b)`: same mode(s), same addresses (set equality incl. prefix), same gateway, same DNS
list (order-sensitive). For `Automatic` address mode only the mode (not the leased address) is
compared. Used by verification (D3) and by the "was it changed by someone else" check (D6).

`AdapterConfig.FromSettings(adapter, AddressSettings)` builds the target configuration (static, one
address, optional gateway, static DNS if any else *static empty*).

## SavedConfiguration (the persisted record, see contracts/restore-file.md)

| Field | Meaning |
|-------|---------|
| `Previous` | `AdapterConfig` before the first change |
| `Applied` | `AdapterConfig` that the program set (target) |
| `CreatedUtc` | when the record was written |

State: absent → written (before step 1) → deleted (after verified restore, or discard, or "left as
is"). A present file at start-up = *Unfinished Session*.

## ApplyStep (named steps for error reporting)

`WriteRecord, SetAddress, SetGateway(part of SetAddress), SetDns, ClearDns, AddAddress,
SetAddressDhcp, SetDnsDhcp, WaitForAddress, Verify, DeleteRecord`.

`StepResult { Step, Succeeded, ExitCode }`; failure surfaces as
`FailureReason(AddressApplyFailed | AddressRestoreFailed, argument = step name)`.

## ChangePreview (what the confirmation dialog shows)

| Field | Meaning |
|-------|---------|
| `AdapterName` | friendly name |
| `Rows` | list of (label key, current text, new text, changed flag) for: Mode, Address, Mask, Gateway, DNS |

Built by `ChangePreview.Create(AdapterConfig current, AddressSettings proposed)`. Text uses
`Automatic` / `none` markers that the view model localizes (no text in Core).

## ConfigSession state (inside ServerSession)

```text
Stopped --Start(settings)--> Starting --lock+record+apply+verify+wait+gate+bind--> Running
Starting --any step fails--> (rollback restore) --> Failed(reason)
Running --Stop | exit | interface lost--> Stopping --close endpoint, restore, verify--> Stopped
Stopping --restore fails--> Stopped + RestoreFailed notice (record kept)
App start: record present --> Recovering --restore ok--> Idle   |   fail --> Idle + banner (Retry/Discard)
```

`ServerState` gains `Stopping`. `FailureKind` gains `AddressApplyFailed`, `AddressRestoreFailed`,
`SettingsLocked`, `RecordNotWritable`, `AddressNotReady`.

## Abstractions (Principle VI)

`IAdapterConfigService` (`AdapterConfig Read(adapterId)`, `StepResult Execute(step, arguments)` —
real *(App)* implementation uses `netsh`, fake in tests can fail at any step), `IRestoreStore`
(`SavedConfiguration Load()`, `void Save(...)` throwing on failure, `void Delete()`), `ISettingsLock`
(`bool TryAcquire()`, `void Release()`), reuse of `IClock`, `IInterfaceProvider`, `IAddressProber`.
