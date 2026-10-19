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

### Installer (not built yet)

The updater already understands an installer-based installation; what is missing is the installer itself and code
signing. Requirements for it:

- Per-user install to `%LOCALAPPDATA%\Programs\Kumo`, no elevation.
- Writes `Kumo.installation.json` containing `{"kind":"installer"}` next to `Kumo.exe`; the uninstaller removes it.
- Closes or waits for a running Kumo, replaces files atomically or rolls back on failure, then starts Kumo again.
- Product version resource equal to the release version (Kumo checks it before running the installer).
- Authenticode-signed with the certificate whose subject is configured as `KUMO_UPDATE_PUBLISHER`.
- Asset name `Kumo-<version>-<rid>-setup.exe`, listed in `SHA256SUMS.txt`.

Inno Setup and WiX both meet these requirements; neither is installed on the current development machine, so no
installer project is checked in yet.

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
