# Versioning

Kumo follows [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## One canonical version

The version lives in exactly one place: `<Version>` in `Directory.Build.props`. Everything else derives from it:

| Where | Value for `0.2.0-beta.1` | How |
| --- | --- | --- |
| Assembly informational version (what Kumo reports) | `0.2.0-beta.1+<commit>` | SDK, with the source revision appended |
| Assembly / file version (Windows) | `0.2.0.0` | SDK, numeric part only |
| `src/Kumo.App/app.manifest` | `0.2.0.0` | kept in sync, checked by `tools/check-version.ps1` |
| Release tag | `v0.2.0-beta.1` | created by a maintainer, checked by the release workflow |
| Release assets | `Kumo-0.2.0-beta.1-win-x64.zip` | `tools/package-release.ps1` |
| MSIX package | `0.2.0.0` | stable versions only (see [distribution.md](distribution.md)) |
| About and Settings | `0.2.0-beta.1 (abc1234)` | informational version with the short commit |

Windows file and package versions are four numbers and cannot express pre-releases, so they carry only
`Major.Minor.Patch.0`. They are never used for update decisions; Kumo compares the informational version.

`tools/check-version.ps1` fails the build if the props version is not strict SemVer, if `app.manifest` disagrees,
if a pre-release stage is not one of `beta`, `rc`, `alpha` or `dev`, or (with `-Tag`) if the tag does not match or
names a `dev` version.

## Precedence

`Kumo.Core.Updates.SemVersion` implements SemVer precedence exactly; strings and `System.Version` are never compared.

- Major, minor and patch compare numerically: `1.10.0 > 1.9.0`, `0.1.10 > 0.1.9`.
- A pre-release ranks below its release: `0.2.0 > 0.2.0-beta.5`.
- Pre-release identifiers compare left to right; numeric identifiers numerically, others in ASCII order, numeric
  below alphanumeric, and more identifiers rank higher when all earlier ones are equal:
  `0.2.0-rc.1 > 0.2.0-beta.9`, `0.2.0-beta.10 > 0.2.0-beta.9`.
- Build metadata (`+...`) is ignored for precedence.

Parsing is strict: no leading zeros, no empty identifiers, three numeric parts. Tags must be `v` + version.

## Pre-release stages

| Stage | Meaning | Offered to |
| --- | --- | --- |
| `beta.N` | Feature-complete preview | Beta and Development channels |
| `rc.N` | Release candidate | Beta and Development channels |
| `alpha.N` | Early preview | Development channel |
| `dev.N` | CI development build, `N` = workflow run number so later builds sort higher | Development channel if ever published; today CI artifacts only and never tagged |

After a release, raise `<Version>` to the next planned version so development builds of `main`
(`0.1.1-dev.N`) sort above the release they follow.

## Choosing the next version

- Bug fixes only: patch (`0.2.0` → `0.2.1`).
- New features or behaviour changes: minor (`0.2.1` → `0.3.0`). Before 1.0, minor versions may also break settings
  compatibility; that is called out in the release notes.
- Pre-releases count up from `.1` and end in a release with the same core version: `0.3.0-beta.1`, `0.3.0-beta.2`,
  `0.3.0-rc.1`, `0.3.0`.
