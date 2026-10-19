# Releases

## Channels

| Channel | Who | Receives |
| --- | --- | --- |
| **Stable** (default) | Everyone | Published GitHub releases whose version has no pre-release part |
| **Beta** (opt-in, Settings → Updates) | Testers | Stable releases plus published `beta` and `rc` pre-releases |
| **Development** | Developers | CI artifacts (`kumo-dev-<sha>`) downloaded by hand; never offered by the updater |

Within a channel Kumo picks the highest SemVer version above its own, not the most recently published release, and
never moves to a lower version. A beta user who returns to Stable stays on the beta they have until a higher stable
version exists.

## Release conventions

A release is a **published GitHub Release** (not just a tag) in the upstream repository:

| Field | Convention |
| --- | --- |
| Tag | `v<version>`, e.g. `v0.2.0`, `v0.2.0-beta.1` |
| Pre-release flag | Set if and only if the version has a pre-release part. Releases where flag and version disagree are ignored by the updater. |
| Title | `Kumo <version>` |
| Notes | Markdown body; shown in Kumo's update window |
| Portable assets | `Kumo-<version>-win-x64.zip`, `Kumo-<version>-win-arm64.zip` |
| Installer assets (future) | `Kumo-<version>-win-x64-setup.exe`, `Kumo-<version>-win-arm64-setup.exe`, Authenticode-signed |
| Integrity | `SHA256SUMS.txt`, lines of `<sha256>  <file name>` covering every other asset |

A release without an asset for a given installation type and architecture is skipped for those installations, and
the next lower eligible version is considered instead.

## Procedure

1. Update `<Version>` in `Directory.Build.props` and `assemblyIdentity` in `src/Kumo.App/app.manifest`; run
   `pwsh tools/check-version.ps1`.
2. Update the documentation and commit on `main`; wait for CI to pass.
3. Create and push an annotated tag: `git tag -a v0.2.0 -m "Kumo 0.2.0"` then `git push origin v0.2.0`.
4. The **Release** workflow validates the tag against the version, builds, runs all tests, packages both
   architectures and writes `SHA256SUMS.txt`.
5. The `draft` job waits for approval in the protected `release` environment, re-checks the checksums and creates a
   **draft** GitHub Release marked as pre-release when appropriate.
6. A maintainer downloads and smoke-tests the assets, edits the notes and publishes the draft. Only now can users see
   it.

### Repository setup (one-time)

- Settings → Environments → `release`: add required reviewers and restrict it to `v*` tags.
- Settings → Rules: protect `v*` tags so only maintainers can create them.
- Settings → Actions → General: workflow permissions "Read repository contents".
- Optional variable `KUMO_UPDATE_PUBLISHER`: the exact subject of the installer signing certificate. Leave it unset
  until installers are signed; without it Kumo never installs updates by itself.

### Signing

There is no signing yet, and no keys or certificates are stored in this repository. When signing is introduced
(Azure Trusted Signing or a certificate held in a hardware-backed CI secret), add a signing step to the `build` job
after `package-release.ps1` publishes the files and before the ZIPs and `SHA256SUMS.txt` are created, so the checksums
cover the signed binaries. Then set `KUMO_UPDATE_PUBLISHER`.

## Rollback

- A bad release that is not yet published: delete the draft.
- A published release: publish a fixed patch version. If that takes time, unpublish the release (convert to draft) so
  no new installations pick it up. Kumo never downgrades automatically, so users already on the bad version need the
  fix release; it can be a higher patch number with the previous code.
- Never re-use a tag or replace assets of a published release; checksums and caches assume releases are immutable.
