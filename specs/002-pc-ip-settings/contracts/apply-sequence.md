# Contract: apply / restore / recovery sequences

Principle II as an ordered, testable procedure. A *step* is the unit named in error messages. All
steps act on the chosen adapter only; no other adapter, firewall rule, service, driver or registry
value is touched (the registry is only *read*).

## Apply (inside Start, when the user chose a fixed address and confirmed the preview)

| # | Step | On failure |
|---|------|------------|
| 0 | Pre-checks: no pending record; adapter exists, is Ethernet/Wi-Fi, is Up; settings valid; no overlap with other active interfaces | refuse, nothing changed |
| 1 | `Lock`: take the settings mutex | `SettingsLocked`, nothing changed |
| 2 | Capture `Previous` (read adapter) | `AddressApplyFailed(Capture)`, release lock, nothing changed |
| 3 | `WriteRecord` (previous + applied target) | `RecordNotWritable`, release lock, nothing changed |
| 4 | `SetAddress` (address, mask, gateway or none) | rollback |
| 5 | `SetDns` / `ClearDns` | rollback |
| 6 | `Verify` (re-read, compare with target) | rollback |
| 7 | `WaitForAddress` (≤ 10 s until Preferred) | rollback |
| 8 | continue with the start gate of feature 001 (re-read interface, eligibility, other-server probe, bind) | rollback |

**Rollback** = the Restore sequence below. After a failed apply the server is never running
(FR-011); the final state is `Failed(reason)` where reason names the failing step. If the rollback
itself fails, the record stays and the banner offers Retry/Discard (reason shows both steps).

## Restore (Stop, exit, interface lost, rollback, recovery, Retry)

| # | Step | On failure |
|---|------|------------|
| 0 | Close the server endpoint first (if any) | — |
| 1 | Read the adapter by GUID; not found ⇒ keep record, report "adapter missing", stop here | record kept |
| 2 | Compare current with `Applied` (D6). Different ⇒ leave as is, delete record, report "left unchanged" | — |
| 3 | `SetAddress` / `SetAddressDhcp` to `Previous.mode`; for static: first address via `SetAddress`, further ones via `AddAddress` | `AddressRestoreFailed(step)`, record kept |
| 4 | `SetDns` / `ClearDns` / `SetDnsDhcp` per `Previous.dnsmode` | same |
| 5 | `Verify` against `Previous` (DHCP: mode only) | same |
| 6 | `DeleteRecord`, release lock | `AddressRestoreFailed(DeleteRecord)` |
| 7 | state `Stopped`, report "restored" | — |

The restore never starts a server and is idempotent: running it twice, or after a partial restore,
ends in the same state (a half-restored adapter simply differs from `Applied` ⇒ if the difference is
caused by our own partial restore the compare in step 2 would wrongly say "changed"; therefore step 2
is skipped when the record is marked `restoring=1`, a line the program adds before step 3).

## Startup recovery

1. If `DhcpServer.restore` exists: take the lock (else `SettingsLocked` banner), run Restore.
2. Success / "left unchanged": notice, normal UI.
3. Failure or adapter missing: banner with **Retry** and **Discard**; Start is disabled until the
   record is gone. *Discard* asks for confirmation (the old settings will not be restored).

## What the user sees (message keys in `ui-contract.md`)

Apply failed: `Reason_AddressApplyFailed` + step name (`Step_*`); restore failed:
`Reason_AddressRestoreFailed` + step name and the previous settings as text so the user can type
them by hand if needed; success: `Notice_SettingsApplied` / `Notice_SettingsRestored`.
