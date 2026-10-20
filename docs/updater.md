# Updater

## Components

| Component | Project | Role |
| --- | --- | --- |
| `SemVersion` | Core | Strict SemVer 2.0.0 parsing and precedence |
| `ReleasePlatform`, `ReleaseTag` | Core | Platform-prefixed tags (`windows/v…`, `linux/v…`); releases of other platforms are ignored |
| `ReleaseSelector`, `ReleaseAssets` | Core | Channel eligibility, asset naming, choosing the highest eligible version |
| `GitHubReleaseSource` (`IUpdateSource`) | Core | Paginated, unauthenticated GitHub REST listing with error classification |
| `UpdateDownloader` | Core | HTTPS downloads with host allow-list, size limits, streaming SHA-256 |
| `UpdateService`, `UpdateStateStore` | Core | When to check, single in-flight check, notification and snooze state |
| `InstallationSource`, `InstallerPolicy` | Core | Who owns updates; whether a downloaded installer may run |
| `Authenticode`, `PackageIdentity` | Windows | `WinVerifyTrust` signature check; MSIX identity via `GetCurrentPackageFullName` |
| `UpdateCoordinator`, `UpdateWindow` | App | Wiring, the update window, installer handoff |

## Who updates what

| Installation | How it is recognised | Behaviour |
| --- | --- | --- |
| Microsoft Store | Package identity and `Package.Current.SignatureKind == Store` | Kumo never contacts GitHub. Settings show "Updates managed by Microsoft Store." |
| MSIX outside the Store | Package identity, not Store-signed | Updates are announced; the button opens the release page. A GitHub installer is never run over a package. |
| Kumo installer | `Kumo.installation.json` with `{"kind":"installer"}` next to `Kumo.exe`, written by the installer | Verified download and handoff, **only if** the build has an expected publisher; otherwise announced like portable. |
| Portable ZIP | Neither of the above | Updates are announced; the button opens the release page. |
| Any build without a configured repository | `KumoUpdateRepository` empty (all local builds) | No checks at all; Settings say so. |

Folder names and install paths are never used to guess the installation type.

## When Kumo checks

- Once, about 30 seconds after start-up, if automatic checks are on (Settings → Updates; on by default).
- Again only when Kumo is used (the menu opens) and the last automatic check is more than 24 hours old. There is no
  timer between checks.
- On "Check for updates" in Settings or the tray menu, regardless of the 24-hour limit, even with automatic checks
  off.
- Immediately after the update channel changes; the previous channel's cached result is discarded. A check already
  running for the old channel finishes first, then the new channel is checked.
- If GitHub reports a rate limit, automatic checks wait until its reset time.
- Concurrent requests for the same channel share one in-flight check.

Each check is one to three unauthenticated GET requests to `api.github.com` with the user agent
`Kumo/<version> (+https://github.com/<owner>/<repo>)`. Nothing about the user or their files is sent. The HTTP
client is created on first use and its connections close after a minute of inactivity.

Stored state (`%LOCALAPPDATA%\Kumo\updates.json`): last check time, rate-limit reset time, last announced version,
and a snoozed version with its date. Nothing else.

## Status shown in Settings

Settings → Updates follows the update service: "Kumo hasn't checked for updates yet", "Checking for updates…",
"Kumo is up to date." (or the pre-release explanation above), "Kumo 0.2.0-beta.1 is available." with **View update**,
a short failure message ("Couldn't check for updates. Try again later."), or "Updates managed by Microsoft Store.".
Downloading and ready-to-install states appear in the update window, and only for installations that may install
verified updates. The installation source reads Microsoft Store, GitHub (Kumo's installer), Portable, or Unknown
(an MSIX package from outside the Store).

## Notifications

An automatic check that finds a new version shows one tray notification ("Kumo 0.2.0 is available"). The same version
is not announced again. "Later" in the update window snoozes that version for three days; a higher version is
announced immediately. Notifications respect Settings → Notifications.

## Trust model

| Threat | Protection |
| --- | --- |
| Update metadata from somewhere else | The repository is fixed at build time (`KumoUpdateRepository`, set by CI to the repository that built the release). Users cannot point Kumo at another source. |
| Downloads from unexpected hosts | Only `https` on the default port to `github.com`, `objects.githubusercontent.com` and `release-assets.githubusercontent.com`. Redirects are followed by hand (at most five) and every hop is checked. |
| Corrupted or truncated download | Size must match the release metadata and stay under 300 MB; SHA-256 must match `SHA256SUMS.txt`; the file only gets its final name after both checks. |
| Compromised release (attacker can upload assets) | Checksums alone do **not** help here: they come from the same release. With a publisher configured, the installer must carry a valid Authenticode signature (chain and revocation checked by `WinVerifyTrust`) whose subject equals the publisher compiled into the build, and its product version must equal the offered version. Anything else is deleted unrun. |
| Path tricks in asset names | Asset names must match `^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$` and the resolved path must stay inside `%LOCALAPPDATA%\Kumo\updates\<version>`. |
| Elevation | The installer is started with `ShellExecute` and no `runas`; Kumo's per-user model never needs administrator rights. |
| Interrupting work | The user starts the update with "Update" or "Get update". Once the download is checked, the installer runs with `/S /relaunch` and Kumo exits; if file operations are running, Kumo waits for "Install now" instead. The installer closes Kumo, replaces the files (rolling back on failure) and starts the new version. Settings in `%LOCALAPPDATA%\Kumo` are not touched. |
| Interrupted download or install | Partial files are deleted on failure or cancellation; the whole download folder is removed at the next start. The installer is responsible for rolling back its own file changes. |

## Trust without a signature

Installers are not signed yet. Copies installed with the setup still update themselves (`InstallChecked`), a choice
the maintainer made explicitly on 2026-10-20 in favour of one-click updates:

- the setup must come from a release of the repository compiled into the build, downloaded from GitHub hosts only;
- its SHA-256 must match `SHA256SUMS.txt` of the same release, and its product version must equal the offered version;
- it runs per-user, without elevation, silently, and restarts Kumo.

This protects against corrupted, truncated or mismatched downloads, **not** against a compromised release: anyone
able to publish a release in the repository could ship an installer that Kumo would run. Protecting the repository
(2FA, protected `windows/v*` tags, the approval environment for releases) is therefore part of update security.
Once installers are signed, setting `KUMO_UPDATE_PUBLISHER` switches to `InstallVerified` and the signature check
without code changes. Portable copies only announce updates.

## Current limitations

- Revocation checking needs network access; offline machines cannot verify an installer and will not install.
- Release notes are shown as plain text.
- Only the first three pages (300) of releases are read.
