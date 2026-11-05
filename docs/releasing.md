# Releasing DhcpServer

Releases are built by GitHub Actions (`.github/workflows/release.yml`) from a version tag on
`main` and created as **drafts**. A draft becomes public only when the maintainer publishes it
after the manual test checklist has passed (see the constitution).

Published releases are **immutable**: their tag and files can never change. A mistake in a
published release is fixed by releasing a new version.

## 1. Write the release notes

In a pull request, add a section to [`release-notes.md`](release-notes.md):

```markdown
## 1.2.0 — short title (YYYY-MM-DD)

What changed, verification status, …
```

- The heading must start with `## ` followed by the exact version (`1.2.0`, or `1.2.0-rc.1`
  for a pre-release).
- Everything up to the next `## ` heading becomes the release text.

Merge the pull request (squash).

## 2. Tag the release

```powershell
git switch main
git pull --ff-only
git tag v1.2.0          # or v1.2.0-rc.1 for a pre-release
git push origin v1.2.0
```

Only the repository owner can create `v*` tags. The `Release` workflow then:

1. Checks that the tag is `vX.Y.Z[-suffix]`, that its commit is on `main`, that the notes section
   exists, and that no release exists for the tag.
2. Builds and tests that commit exactly as CI does, with the version stamped into the
   executable.
3. Creates a **draft** release with `DhcpServer.exe`, `DhcpServer.exe.config` and `SHA256SUMS`.
   Tags with a suffix produce a pre-release.

If any step fails, nothing is published. Fix the problem and release a **new** version; do not
reuse the tag.

## 3. Manual test checklist

Download the draft's `DhcpServer.exe` and check it:

```powershell
Get-FileHash .\DhcpServer.exe        # must equal the DhcpServer.exe line in SHA256SUMS
```

Then:

1. Confirm that the About dialog shows the tagged version.
2. Run [`manual-test-checklist.md`](manual-test-checklist.md) on Windows 7 SP1 and Windows 11
   with this exact file.

## 4. Publish

Edit the draft on GitHub:

1. Add the checklist results (date, OS, pass/fail per step).
2. Remove the "Manual test checklist pending" banner.
3. Publish.

From this moment the release is final. If the checklist fails, delete the draft, fix the
problem, and release a new version.

## Repository protection

Apply once after the repository is created on GitHub (repository admin, authenticated `gh`):

```powershell
pwsh tools/github/Set-RepoProtection.ps1          # creates/updates the rulesets
pwsh tools/github/Set-RepoProtection.ps1 -Show    # exit 0 = rules match the files
```

`main` then accepts changes only through pull requests with a green `build-test` check, merged
by squash.

- **CI is broken and blocks every pull request**: open the pull request with the fix and, as the
  owner, choose **"Merge without waiting for requirements to be met (bypass rules)"**.
- **A direct push to `main` is unavoidable**:
  ```powershell
  pwsh tools/github/Set-RepoProtection.ps1 -Suspend main-protection   # asks for the name to confirm
  # … push …
  pwsh tools/github/Set-RepoProtection.ps1                            # re-applies all rules
  ```

The rules live in [`.github/rulesets/`](../.github/rulesets/). Change them through a pull
request, then re-run the script.
