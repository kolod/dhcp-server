# Contract: restore file `DhcpServer.restore`

Location: the folder of the executable. Encoding: UTF-8 without BOM, `\r\n` on write, any on read.
Unlike settings and leases, **failing to write this file is fatal for applying** (FR-014): nothing
is changed unless the record is safely on disk (written to a temp file, flushed, moved over).

```text
#DhcpServer restore v1
created=2026-11-05T19:30:00Z
adapter=<adapter GUID>
index=<interface index>
name=<friendly name, informational>
previous.mode=static|dhcp
previous.address=<ip>/<prefix>        (repeat the line for every address, in order)
previous.gateway=<ip>|none
previous.dnsmode=static|dhcp
previous.dns=<ip>                     (repeat, in order; absent when none)
applied.mode=static
applied.address=<ip>/<prefix>
applied.gateway=<ip>|none
applied.dnsmode=static
applied.dns=<ip>                      (repeat, in order; absent when none)
```

Rules:

- The first line must be the v1 header, otherwise the file is **treated as corrupt**: recovery shows
  the banner "cannot read the saved settings" with *Discard* only (nothing can be restored safely).
- Unknown keys are ignored; missing `adapter`, `previous.mode` or `applied.mode` ⇒ corrupt.
- `restoring=1` is an optional line added (atomically) before the first restore step; while it is
  present the "changed by someone else" comparison is skipped, so a restore interrupted halfway
  can be finished later (see `apply-sequence.md`).
- `previous.mode=dhcp` has no `previous.address` lines (the leased address is not restored).
- The file exists only while settings are changed or a restore is pending. It is deleted after a
  verified restore, after "left as is" (someone else changed the adapter), or after *Discard*.
- At most one record exists. Applying while a record exists is refused (`SettingsLocked`-style
  banner: finish or discard the pending restore first).
