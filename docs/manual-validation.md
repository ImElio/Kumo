# Manual validation checklist

Status legend: **Automated** = covered by `tools/Kumo.DragProbe` or UI Automation runs with real input;
**Pending** = must be checked by a person on real hardware. Nothing below is marked as passed unless it was
actually executed. Last updated 2026-10-19 on Windows 11 Pro 10.0.26200, one monitor at 100 %.

| Check | How | Status |
| --- | --- | --- |
| Drag from a File Explorer window, tap Shift, drop on an action | Probe `drop` | Automated (5 of 6 runs passed, see [interaction.md](interaction.md#verification)) |
| Escape cancels | Probe `escape` | Automated |
| Release elsewhere leaves files untouched | Probe `elsewhere` | Automated |
| Holding Shift keeps Explorer's move behaviour | Probe `hold` | Automated |
| Keyboard shortcut with an Explorer selection | Probe `hotkey` | Automated |
| Click/keyboard menu, submenus, result state | UI Automation invoke | Automated |
| Drag from the **desktop** | Drag a desktop file, tap Shift | Pending |
| Shortcut with a **desktop** selection | Select a desktop file, press Ctrl+Alt+K | Pending |
| **Multiple files** of mixed types | Drag 3 PNG + 1 JPG; check only common actions appear | Pending (covered by unit tests only) |
| Hold-Shift mode | Settings → Open while dragging → Hold Shift | Pending |
| Drag cancellation by right-click or Alt+Tab during the drag | | Pending |
| **Multiple monitors**, mixed scaling | Open the menu on each monitor; check size, position, crispness | Pending |
| 100 / 125 / 150 / 200 % scaling | Change display scale, reopen menu | Pending (only 100 % tested) |
| Menu near screen edges and the taskbar | Activate at each corner | Pending (top edge clamping seen working) |
| **Tray**: icon, left click, menu items, About, Settings | | Pending |
| No taskbar button while idle or while the menu is open | | Pending (Settings and About windows show one by design) |
| **Start with Windows** on and off | Toggle, sign out and in | Pending |
| Second launch hands files to the running instance | `Kumo.exe file.png` | Automated (log and UI Automation) |
| **Explorer restart** re-adds the tray icon | `taskkill /f /im explorer.exe`, restart Explorer | Pending |
| **Sleep / resume** keeps the gesture working | | Pending |
| Lock / unlock, fast user switching | | Pending |
| File permission errors | Drop a file from a folder you can read but not write | Pending (unit-tested via exception mapping) |
| Locked files | Open a file exclusively in another app, then convert | Automated in `Kumo.Operations.Tests` |
| Large files | 100+ MP image, 500-page PDF | Pending |
| Repeated operations | 50 conversions in a row; watch memory in Task Manager | Partially automated (13 menu cycles, no growth) |
| High contrast | Turn on a contrast theme | Pending |
| Reduced motion | Settings → Accessibility → Animation effects off | Pending |
| Narrator reads segments and status | | Pending |
| Light and dark themes | | Checked visually from screenshots |
| Remote Desktop session | | Pending |
| Pen and touch drags | | Pending |
| Windows 10 22H2 | | Pending |

## Updates

| Check | How | Status |
| --- | --- | --- |
| Local build shows "update checks aren't available" | Settings → Updates | Checked (screenshot); the hidden tray item was not checked by hand |
| Real check against a published release | CI build of a tagged release with at least two published releases | Pending (no repository or releases yet) |
| Settings → Updates in an update-enabled build: channel switch re-checks, status changes, Development warning, View update | Build with a configured repository | Pending (states unit-tested; only the disabled local-build page was checked on screen) |
| Settings written by earlier builds (`"UpdateChannel": "Stable"`) load as Release without resetting other settings | Start the new build on an existing profile | Checked on the development machine |
| Beta channel picks the highest beta/rc | Same, with pre-releases | Pending (unit-tested with mocked HTTP) |
| Rate limit message and back-off | Exhaust the unauthenticated limit | Pending (unit-tested) |
| Offline check shows the connection message | Disconnect, Check for updates | Pending (unit-tested) |
| Tray notification once per version, Later snoozes | Automatic check with an available update | Pending (unit-tested) |
| Installer: refuses a folder not named Kumo, fresh install, marker, uninstall entry, shortcut | Silent installs into a test folder | Automated run passed (2026-10-19) |
| Installer: Kumo detects the installer installation | Same run; Kumo's log shows `"installation":"Installer"` | Automated run passed |
| Installer: upgrade over a running Kumo, busy executable left intact, uninstall keeps settings | Same run | Automated run passed |
| Arm64 installer refuses x64 PCs | Silent run on x64 | Automated run passed |
| Interactive installer pages, Finish → Start Kumo, Installed apps entry | Run the installer by hand | Pending (first page checked from a screenshot) |
| Installer on Windows on Arm | Arm64 PC | Pending |
| Signed installer download, verification and handoff | Requires a signed installer release and `KUMO_UPDATE_PUBLISHER` | Blocked: no signing yet |
| Store-installed copy shows "Updates managed by Microsoft Store" | Install from the Store | Blocked: not in the Store yet |
| Release workflow end to end | Push a tag in a test fork with the `release` environment configured | Pending |
