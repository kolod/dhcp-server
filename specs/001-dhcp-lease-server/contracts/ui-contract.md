# Contract: main window and localization keys

## Layout (single window, auto-sized grid, no fixed text widths — Principle V)

```text
┌──────────────────────────────────────────────────────────────┐
│ Interface: [ComboBox: name — IPv4 — status ▾]  [Start/Stop]  │
│ Language: [ComboBox]                                         │
│ Status: ● <state text>  (reason / hint line, wraps)          │
├──────────────────────────────────────────────────────────────┤
│ Leases                                                       │
│ ┌────────────┬───────────────────┬───────────┬───────┬──────┐│
│ │ IP address │ MAC address       │ Host name │ Start │Expiry││
│ │ …          │ …                 │ …         │ …     │ …    ││
│ └────────────┴───────────────────┴───────────┴───────┴──────┘│
│ (empty-state text when no leases)                            │
└──────────────────────────────────────────────────────────────┘
```

## Behavior

- **Interface list**: every adapter appears as `name — IPv4 (or "no address") — status`. Unavailable
  adapters are shown greyed, cannot be selected, and show the reason in a tooltip and in the status
  line when hovered/focused. The list refreshes on network changes while the server is stopped.
- **Selection** is remembered in `DhcpServer.ini` (`LastInterface=<id>`) and preselected if still
  present; the server is never started automatically (FR-018). While running, the selector is disabled.
- **Start/Stop**: one button; label follows state. Start with no selection is disabled with a hint.
  Starting shows `Starting…` (probe takes up to ~6 s) and the button becomes Cancel; cancelling
  aborts the probe and returns to `Stopped`.
- **Lease list**: sorted by IP address; active rows normal, inactive rows dimmed with the state text
  in an extra column (`Active`, `Expired`, `Released`, or `Inactive` while the server is stopped).
  Updates in place (no flicker, selection preserved). Times in local time, short format.
- **Copy**: Ctrl+C on a selected row copies `IP<TAB>MAC`; context menu has *Copy IP address* and
  *Copy MAC address*.
- **Notices** (non-blocking, in the status area): pool exhausted; lease file not writable.
- **Failure** (state `Failed`): status shows the reason; nothing is listening.
- **Accessibility**: all controls have automation names from the same string keys; the state is not
  conveyed by color alone (text + glyph).

## State → status text

| State | Key |
|-------|-----|
| Stopped | `Status_Stopped` |
| Starting | `Status_Starting` |
| Running | `Status_Running` (formatted with address) |
| Failed | `Status_Failed` + `Reason_*` |

## New string keys (each added to every `Strings*.resx` with a real translation)

Interface/controls: `Interface_Label`, `Interface_NoAddress`, `Interface_Status_Up`,
`Interface_Status_Down`, `Button_Start`, `Button_Stop`, `Button_Cancel`, `Hint_ChooseInterface`.

Status: `Status_Stopped`, `Status_Starting`, `Status_Running`, `Status_Failed`, `Notice_PoolExhausted`,
`Notice_LeaseFileNotWritable`, `Notice_FirewallHint`.

Reasons (interface eligibility and start failures): `Reason_Down`, `Reason_NotSupportedType`,
`Reason_NoIpv4Address`, `Reason_LinkLocalAddress`, `Reason_SubnetTooSmall`, `Reason_SubnetOverlap`,
`Reason_NoInterfaceChosen`, `Reason_OtherDhcpServer`, `Reason_ProbeInconclusive`,
`Reason_PortInUse`, `Reason_AccessDenied`, `Reason_InterfaceLost`, `Reason_Unexpected`.

Lease list: `Leases_Header`, `Leases_Empty`, `Column_IpAddress`, `Column_MacAddress`,
`Column_HostName`, `Column_Start`, `Column_Expires`, `Column_State`, `LeaseState_Active`,
`LeaseState_Expired`, `LeaseState_Released`, `LeaseState_Inactive`, `Menu_CopyIp`, `Menu_CopyMac`.

Existing keys (language selector, window title) are reused unchanged.
