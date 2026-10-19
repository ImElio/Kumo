# Kumo

<img src="assets/icons/kumo-256.png" alt="Kumo, a small cloud with an orange scarf" width="112" />

Kumo is a small Windows utility for everyday file chores. It waits quietly in the notification area; when you
drag files out of File Explorer and tap **Shift**, a radial menu appears under the pointer. Move onto an action,
let go, and the result is saved next to the original.

Everything runs locally. Kumo has no account, no telemetry, no advertising and makes no network requests.

> Status: early development (0.1). The core interaction and file operations work and are covered by automated
> tests, but Kumo has not yet been validated on a wide range of machines. See [Known limitations](#known-limitations).

## Requirements

- Windows 10 version 2004 (build 19041) or later, x64. So far developed and tested on Windows 11 (build 26200).
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for framework-dependent builds.
- No administrator rights.

## Using Kumo

1. Start `Kumo.exe`. Nothing opens; a cloud icon appears in the notification area and a one-time notification
   explains the gesture.
2. In File Explorer or on the desktop, start dragging one or more files.
3. While still holding the mouse button, **tap Shift** (press and release it quickly).
4. The radial menu opens around the pointer, showing only actions that work for the files you are dragging.
5. Move onto an action and release the mouse button. Resting on a category such as **Convert** for a moment
   opens its formats in the same circle; resting on the centre goes back.
6. Kumo shows progress in the centre of the circle, then a short result with a **Show** button that opens the
   folder with the new file selected.

Holding Shift for the whole drag keeps File Explorer's normal "move" behaviour; only a quick tap opens Kumo.
A "Hold Shift" mode is available in Settings for people who prefer it. See [docs/interaction.md](docs/interaction.md)
for the reasoning and the technical details.

Other ways in, all leading to the same menu:

- **Ctrl+Alt+K** (configurable) opens the menu for the files selected in the active File Explorer window or on the
  desktop.
- **Open Kumo…** in the tray menu (or a left click on the icon) lets you pick files.
- Dropping files on the centre of the circle, or on a category, keeps them in Kumo so you can choose with the mouse
  or keyboard.
- Launching `Kumo.exe file1 file2 …` while Kumo is running opens the menu for those files, so it works with a
  "Send to" shortcut.

### Keyboard and accessibility

When Kumo owns the interaction (shortcut, tray, or after a drop), the arrow keys move between segments, Enter or
Space chooses, Backspace goes back and Escape goes back or closes. Every segment is a real button with a
screen-reader name (verified through UI Automation), and status changes are raised as a live region; a full
pass with Narrator is still on the validation list. Kumo follows high-contrast themes, the Windows animation
setting (reduced motion), light and dark mode, and per-monitor display scaling.

## What Kumo can do

| Files | Action | Result |
| --- | --- | --- |
| PNG, JPG, WEBP, BMP, GIF (first frame) | **Convert** to PNG, JPG, WEBP, BMP or PDF | New file in the chosen format. Transparency is placed on white for JPG and BMP. Camera orientation is applied. |
| Images | **Resize** to 75 %, 50 %, 25 %, or to fit within 1920 or 1280 pixels | Same format (GIF results are saved as PNG). Images already small enough are left alone. |
| JPG, WEBP | **Compress**: High quality, Balanced or Smallest | Re-encoded copy. If it would not be smaller, nothing is saved. |
| PNG | **Compress** (lossless optimisation) | Maximum PNG compression without changing pixels. |
| Images | **Rotate** 90° right, 90° left or 180° | Rotated copy. JPG files are re-encoded at quality 92. |
| Two or more images | **Combine into PDF** | One A4 page per image, sorted by file name. |
| PDF | **Convert** to PNG or JPG | One image per page at 150 DPI, rendered by the PDF engine built into Windows. |
| Two or more PDFs | **Merge PDFs** | One document, sorted by file name the way File Explorer sorts. |
| PDF | **Split** | One PDF per page in a new folder. |
| PDF | **Extract** pages | A copy containing the pages you type, for example `1-3, 5, 8-`. |
| PDF | **Protect** with a password | An AES-256 encrypted copy. |

Safety rules that apply to every action:

- Originals are never overwritten. If a result name exists, Kumo adds a number (configurable to replace an older
  result, but never an original or a file written in the same batch).
- Results are written to a hidden temporary file and only renamed into place when complete, so cancelled or failed
  work leaves nothing behind.
- File types are detected from their content, not their extension.
- Oversized inputs (over 1 GB, over 200 megapixels, PDFs over 5,000 pages) are refused before decoding, which also
  protects against decompression bombs.
- Each file in a batch succeeds or fails on its own; at most 1–4 files are processed at once (default 2).
- Locked files, missing permissions, full disks, damaged files and password-protected PDFs produce short,
  specific messages.
- "Keep original files" can be turned off; originals then go to the Recycle Bin after a successful result.

Not supported yet: DOCX, audio, video, TIFF, HEIC, cropping, and PDF text extraction.

## Settings

Right-click the tray icon → **Settings**: start with Windows, theme, notifications, activation mode (tap Shift,
hold Shift, off), menu size, motion, keyboard shortcut, output folder, keeping originals, name collisions,
files processed at once, and diagnostic logging.

Settings are stored in `%LOCALAPPDATA%\Kumo\settings.json` (written atomically). Logs are JSON lines in
`%LOCALAPPDATA%\Kumo\logs`, kept for seven days; file paths only appear when detailed logging is enabled.
"Start with Windows" is a per-user `HKCU\…\Run` entry and is off until you turn it on.

## Building

Requires the .NET 10 SDK (10.0.400 or later) on Windows.

```powershell
dotnet build Kumo.slnx
dotnet test Kumo.slnx
dotnet run --project src/Kumo.App
```

Release output (framework-dependent, about 38 MB):

```powershell
dotnet publish src/Kumo.App -c Release -r win-x64 --self-contained false -o artifacts/publish
```

`tools/Kumo.AssetGen` regenerates the mascot SVGs, the state sheet and `kumo.ico` from the artwork defined in code.
`tools/Kumo.DragProbe` drives real drags out of File Explorer to verify the integration end to end; it moves the
pointer for a few seconds (see [docs/interaction.md](docs/interaction.md#verification)).

## Project layout

| Path | Contents |
| --- | --- |
| `src/Kumo.Core` | File type detection, operation contracts and executor, output naming and atomic writes, radial menu model, settings, logging. No UI or Win32 dependencies. |
| `src/Kumo.Operations` | Image operations (SkiaSharp) and PDF operations (PDFsharp, Windows.Data.Pdf). |
| `src/Kumo.Windows` | Win32 and shell interop: global input hook, tray icon, hotkey, single instance, autostart, Explorer selection, Recycle Bin, overlay window placement. |
| `src/Kumo.App` | The WPF application: lifecycle, radial overlay, mascot, settings and dialogs. |
| `tests/` | xUnit tests for each library. |
| `tools/` | Asset generator and the Explorer drag probe. |
| `docs/` | [Architecture](docs/architecture.md), [interaction](docs/interaction.md), [performance](docs/performance.md), [manual validation](docs/manual-validation.md), [roadmap](docs/roadmap.md). |

## Performance

Measured on the Release build (details and method in [docs/performance.md](docs/performance.md)):

- Idle: about 15 MB private working set, no measurable CPU (0–31 ms of CPU time over 60 s), no timers or polling.
- Ready for input about 0.2 s after launch; the menu appears in about 10 ms (about 95 ms the very first time).
- A 12-megapixel JPG converts to WEBP in about 0.5 s.

## Known limitations

- The drag gesture only starts from File Explorer windows and the desktop. Drags from other applications are not
  intercepted; use the shortcut or the tray instead.
- Drags from applications running as administrator cannot be dropped on Kumo (Windows blocks drops from elevated
  processes into normal ones).
- While the menu is open it covers the area around the pointer.
- Drags from the desktop, the shortcut on a desktop selection, multiple monitors with mixed scaling, pen/touch
  drags, Remote Desktop and Explorer restarts have not yet been validated by hand. See [docs/manual-validation.md](docs/manual-validation.md).
- English only.
- The mascot is a clean vector placeholder; production artwork is still to be commissioned.
- No installer yet; see the packaging notes in [docs/roadmap.md](docs/roadmap.md).

## License

Kumo is released under the [MIT License](LICENSE). Third-party components keep their own licenses; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
