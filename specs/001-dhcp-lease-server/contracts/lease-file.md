# Contract: lease file `DhcpServer.leases`

Location: the folder of the executable. Encoding: UTF-8 without BOM. Line endings: any on read, `\r\n`
on write. Missing, unreadable or unwritable file is never fatal (research D9).

```text
#DhcpServer leases v1
<mac>\t<ip>\t<prefix>\t<startUtc>\t<expiresUtc>\t<state>\t<hostName>
```

| Column | Format | Example |
|--------|--------|---------|
| `mac` | 12 hex digits, lowercase, no separators | `a1b2c3d4e5f6` |
| `ip` | dotted IPv4 | `192.168.137.12` |
| `prefix` | integer 8–30 | `24` |
| `startUtc`, `expiresUtc` | ISO 8601 `yyyy-MM-ddTHH:mm:ssZ` | `2026-11-05T18:30:00Z` |
| `state` | `A` active / `R` released (expired is derived from the clock) | `A` |
| `hostName` | printable ASCII, tabs/newlines removed, ≤ 63 chars, may be empty | `esp32-kitchen` |

Rules:

- Lines starting with `#` and blank lines are ignored; the first line must be the v1 header, otherwise
  the whole file is ignored (and overwritten on the next save).
- A line with the wrong number of columns or an unparsable value is skipped; other lines still load.
- Duplicate `mac` or `ip`: the entry with the later `expiresUtc` wins.
- Saves go to `DhcpServer.leases.tmp` and are moved over the real file; the temp file is deleted on
  failure. At most one save per second, plus a flush on stop and on exit.
- Entries whose subnet differs from the current session's subnet are kept in the file and shown as
  inactive, but are never considered when allocating addresses.
- The file holds at most 1 024 entries; the oldest inactive entries are dropped first.
