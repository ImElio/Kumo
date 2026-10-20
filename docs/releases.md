# Releases

## Channels

Chosen in Settings → Updates → Update channel. The choice is saved immediately and triggers a fresh check; nothing is
downloaded or installed until the user asks.

| Channel | Shown as | Receives from published GitHub Releases |
| --- | --- | --- |
| **Release** (default) | Release (Recommended) | Stable versions only (`0.1.0`, `0.1.1`, `0.2.0`) |
| **Beta** | Beta (Early access) | Stable plus `beta` and `rc` pre-releases |
| **Development** | Development (Experimental) | Stable plus `beta`, `rc`, `alpha` and `dev` pre-releases |

Within a channel Kumo picks the highest SemVer version above its own, not the most recently published release, and
never moves to a lower version. Someone on `0.3.0-beta.2` who returns to Release while the newest stable release is
`0.2.5` keeps `0.3.0-beta.2`; Settings explain that Kumo will update when a newer stable release exists.

**Development builds are not published as GitHub Releases.** CI produces them as workflow artifacts
(`kumo-dev-<sha>`, versioned `<version>-dev.<run number>`) for manual download, and the release workflow refuses `dev`
tags. Until that changes the Development channel offers alpha, beta and release candidate releases, and the Settings
page says so. Microsoft Store copies ignore channels: the Store owns their updates.

## Platforms

Kumo is released per operating system from this one repository. Each platform has its own tags and its own SemVer
line, so Windows `1.4.0` and Linux `0.2.0` can coexist and are never compared with each other:

| Platform | Tag | Status |
| --- | --- | --- |
| Windows | `windows/v<version>` | released by `.github/workflows/release.yml` |
| Linux | `linux/v<version>` | reserved; no builds, workflow or updater support yet |

The updater keeps only releases whose tag names its own platform, then applies the channel, architecture and
installation-type rules below. A tag without a platform prefix (`v0.1.1`) predates this scheme and is read as a
Windows release; new releases always use the prefix. GitHub's repository-wide "latest release" flag is never read and
never set by the workflow, because with several platforms it would point at whichever platform published last.

`Kumo.Core.Updates.ReleasePlatform` and `ReleaseTag` hold the platform list and tag format. Adding a platform means a
new enum value with its prefix, its own version source, asset names and release workflow.

## Release conventions

A Windows release is a **published GitHub Release** (not just a tag) in the upstream repository:

| Field | Convention |
| --- | --- |
| Tag | `windows/v<version>`, e.g. `windows/v0.2.0`, `windows/v0.2.0-beta.1` |
| Pre-release flag | Set if and only if the version has a pre-release part. Releases where flag and version disagree are ignored by the updater. |
| Title | `Kumo for Windows <version>` |
| Latest flag | Never set ("Set as the latest release" stays unticked when publishing) |
| Notes | Markdown body generated from the previous Windows tag; shown in Settings → Updates and the update window |
| Portable assets | `Kumo-<version>-win-x64.zip`, `Kumo-<version>-win-arm64.zip` |
| Installer assets | `Kumo-<version>-win-x64-setup.exe`, `Kumo-<version>-win-arm64-setup.exe` (NSIS); must be Authenticode-signed before Kumo installs them by itself |
| Integrity | `SHA256SUMS.txt`, lines of `<sha256>  <file name>` covering every other asset |

A release without an asset for a given installation type and architecture is skipped for those installations, and
the next lower eligible version is considered instead.

## Procedure

1. Update `<Version>` in `Directory.Build.props` and `assemblyIdentity` in `src/Kumo.App/app.manifest`; run
   `pwsh tools/check-version.ps1`.
2. Update the documentation and commit on `main`; wait for CI to pass.
3. Create and push an annotated tag: `git tag -a windows/v0.2.0 -m "Kumo for Windows 0.2.0"` then
   `git push origin windows/v0.2.0`.
4. The **Release Windows** workflow validates the tag against the version, builds, runs all tests, installs NSIS if the
   runner lacks it, packages ZIPs and installers for both architectures and writes `SHA256SUMS.txt`. Packaging uses
   `-PublicRelease`, so the build fails if the update repository is missing or invalid, and the previous Windows tag
   for generated notes is found by `tools/Kumo.ReleaseTool` with the updater's own SemVer rules.
5. The `draft` job waits for approval in the protected `release` environment, re-checks the checksums and creates a
   **draft** GitHub Release marked as pre-release when appropriate and not marked as latest. Generated notes start at
   the previous Windows tag, so Linux tags never leak into the Windows changelog.
6. A maintainer downloads and smoke-tests the assets, edits the notes and publishes the draft, leaving "Set as the
   latest release" unticked. Only now can users see it.

### Repository setup (one-time)

- Settings → Environments → `release`: add required reviewers and restrict it to `windows/v*` tags (add `linux/v*` when Linux ships).
- Settings → Rules: protect `windows/v*`, `linux/v*` and legacy `v*` tags so only maintainers can create them.
- Settings → Actions → General: workflow permissions "Read repository contents".
- Optional variable `KUMO_UPDATE_PUBLISHER`: the exact subject of the installer signing certificate. Leave it unset
  until installers are signed; without it installed copies still update themselves after the SHA-256 and version
  checks, but nothing authenticates the publisher (see [updater.md](updater.md#trust-without-a-signature)).

### Signing

There is no signing yet, and no keys or certificates are stored in this repository. When signing is introduced
(Azure Trusted Signing or a certificate held in a hardware-backed CI secret), add a signing step to the `build` job
after `dotnet publish` and before the ZIPs, installers and `SHA256SUMS.txt` are created, so the checksums cover the
signed binaries. Pass the signing command to `package-release.ps1 -SignCommand` so NSIS signs the installer and its
uninstaller. Then set `KUMO_UPDATE_PUBLISHER`.

Before the first public release, run the [end-to-end update test](update-testing.md) against a private test
repository.

## Rollback

- A bad release that is not yet published: delete the draft.
- A published release: publish a fixed patch version. If that takes time, unpublish the release (convert to draft) so
  no new installations pick it up. Kumo never downgrades automatically, so users already on the bad version need the
  fix release; it can be a higher patch number with the previous code.
- Never re-use a tag or replace assets of a published release; checksums and caches assume releases are immutable.
