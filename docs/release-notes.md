# Release notes

Each `## X.Y.Z` section is the text of the GitHub release for that version: the release workflow
copies it verbatim (see [releasing.md](releasing.md)). Keep the heading format
`## X.Y.Z — title (date)`. The authoritative checksums are the ones attached to each GitHub
release.

## Unreleased

- DHCP server on a chosen interface that hands out addresses to IoT devices, with a live lease list
  (IP, MAC, host name, start, expiry, state) that is kept between sessions.
- Safety: refuses to start if another DHCP server answers, the interface is down, link-local or
  overlaps another active network, and stops within two seconds when the interface goes away.
