# Distribution

Kumo is planned for two channels: **GitHub Releases** (portable ZIP today, a per-user installer later) and the
**Microsoft Store** (MSIX). Each installation has exactly one owner for updates; see [updater.md](updater.md).

## GitHub Releases

### Portable ZIP (available now)

Produced by `tools/package-release.ps1` for `win-x64` and `win-arm64`. Framework-dependent: the
[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) must be installed.

Upgrading a portable copy:

1. Exit Kumo from the tray icon.
2. Download the new ZIP from the release page Kumo opened, and compare its SHA-256 with `SHA256SUMS.txt`
   (`Get-FileHash Kumo-<version>-win-x64.zip`).
3. Replace the contents of the Kumo folder with the new files (or unzip next to it and delete the old folder).
4. Start Kumo. Settings and logs live in `%LOCALAPPDATA%\Kumo` and are kept. If "Start with Windows" was on and the
   folder moved, turn it off and on again so the startup entry points to the new location.

### Installer (NSIS)

`packaging/installer/Kumo.nsi`, built by `tools/package-release.ps1` with NSIS 3 (`makensis`) into
`Kumo-<version>-win-x64-setup.exe` and `Kumo-<version>-win-arm64-setup.exe`.

| Behaviour | Detail |
| --- | --- |
| Scope | Per user, no administrator rights (`RequestExecutionLevel user`), default `%LOCALAPPDATA%\Programs\Kumo`. |
| Folder guard | The install folder must be named `Kumo` and be new, empty or already contain Kumo. It is checked on the directory page, in `.onInit` and at install time (silent installs skip the page), so the uninstaller can never delete an unrelated folder. |
| Architecture | The Arm64 installer refuses non-Arm PCs; the x64 installer runs on x64 and on Arm (emulated). |
| Runtime | Looks for the .NET 10 Desktop Runtime and offers its download page if missing; installation continues either way. |
| Running Kumo | Runs `Kumo.exe --exit`, waits up to 10 s for the executable to be released, then asks; silent installs give up (exit code 2) without changing anything. |
| Upgrade safety | The existing folder is renamed to `Kumo.previous` before the new files are copied and restored if copying fails or is cancelled (`.onInstFailed`); it is removed only after success. |
| Updater marker | Writes `Kumo.installation.json` (`{"kind":"installer","version":"…"}`), which tells Kumo it may update itself through the installer. |
| Version resource | `ProductVersion` is the SemVer version, which Kumo checks before running a downloaded installer. |
| Registration | Start menu shortcut and an "Installed apps" entry under `HKCU\…\Uninstall\Kumo` (version, size, quiet uninstall string). |
| Uninstall | Exits Kumo, removes the startup entry, shortcut, registry entry and the install folder. Settings and logs in `%LOCALAPPDATA%\Kumo` are kept. |
| Silent use | `Kumo-<version>-win-x64-setup.exe /S [/D=<folder>\Kumo]` and `Uninstall.exe /S`. |
| Signing | `-SignCommand` in `package-release.ps1` (NSIS `!finalize` / `!uninstfinalize`) signs both the installer and the embedded uninstaller once a certificate exists. |

Unsigned installers trigger SmartScreen's "unknown publisher" warning, and Kumo will not run them by itself; until
signing exists users download and run them by hand.

## Microsoft Store (MSIX)

`packaging/msix/AppxManifest.template.xml` is a starting point, deliberately incomplete. Nothing has been submitted.

| Topic | Notes |
| --- | --- |
| Package identity | `Identity/Name` and `Identity/Publisher` come from the product reservation in Partner Center. They are not in the repository because they do not exist yet. |
| Publisher identity | The Store re-signs the package with the publisher in Partner Center; local test packages need a self-signed certificate whose subject equals `Identity/Publisher`, installed in Trusted People. |
| Version mapping | Store packages use `Major.Minor.Patch.0` (revision must be 0). Only stable versions go to the Store; betas use Store package flights if ever needed. Each submission must have a higher version than the last. |
| Updates | The Store installs updates. Kumo detects Store-signed packages and never contacts GitHub or runs an installer. A Store product link can be added to Settings once the product ID is known. |
| Start-up | Packaged apps cannot write the `HKCU\…\Run` key effectively; "Start with Windows" must move to the `windows.startupTask` extension and the `StartupTask` WinRT API before submission. |
| Capabilities | `runFullTrust` (classic desktop app). Low-level input hooks, `RegisterHotKey`, the notification area icon and OLE drag-and-drop work in full-trust packages. |
| Storage | `%LOCALAPPDATA%` writes from a packaged app are redirected to the package's private folder; settings will not be shared with a portable copy. |
| Certification | Privacy policy URL (Kumo collects nothing, but the Store requires the URL), age rating, screenshots, the WACK test (`appcert.exe`) passing, no crashes on launch, and a working uninstall. The Explorer drag gesture should be explained in the listing because it is not discoverable. |

### Differences between MSIX and unpackaged installs

| | Portable / installer | MSIX |
| --- | --- | --- |
| Updates | GitHub, via Kumo | Microsoft Store |
| Start with Windows | `HKCU\…\Run` | StartupTask (to be implemented) |
| Settings location | `%LOCALAPPDATA%\Kumo` | Package-private `LocalCache` |
| Uninstall | Delete folder / uninstaller | Windows Settings → Apps |
| Identity check | Installer marker file | `GetCurrentPackageFullName` and package signature kind |

## Local development

None of this requires a Microsoft or GitHub account: local builds have no update repository and never check for
updates, and the MSIX template is not part of the build.
