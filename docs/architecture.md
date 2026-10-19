# Architecture

Kumo is a modular monolith: one process, four libraries with one-way dependencies, no plugin system and no
dependency-injection container. Objects are composed by hand in `KumoApplication`.

```
Kumo.App  ──►  Kumo.Operations  ──►  Kumo.Core
   │                                   ▲
   └──────►  Kumo.Windows               │
   └───────────────────────────────────┘
```

| Project | Target | Depends on | Responsibility |
| --- | --- | --- | --- |
| `Kumo.Core` | `net10.0` | – | Pure logic: file kinds, operation contracts, registry, executor, output writer, radial geometry and menu model, settings, logging, user-facing summaries. |
| `Kumo.Windows` | `net10.0-windows` | – | Win32 and shell interop only. No WPF, so it can be tested and reasoned about in isolation. |
| `Kumo.Operations` | `net10.0-windows10.0.19041.0` | Core, SkiaSharp, PDFsharp | Concrete image and PDF operations. The Windows SDK target is needed for `Windows.Data.Pdf`. |
| `Kumo.App` | `net10.0-windows10.0.19041.0`, WPF | all of the above | Application lifecycle, overlay, mascot, windows. |

The brief proposed a separate `Kumo.Infrastructure` project. Settings persistence and logging are small enough
that a separate assembly would only add load time and indirection, so they live in `Kumo.Core` behind plain
classes (`SettingsStore`, `Log`).

## Process lifecycle

`Program.Main` creates a `SingleInstance` (a session-local named mutex plus a named pipe restricted to the current
user). A second launch forwards its arguments over the pipe and exits. The primary instance runs
`KumoApplication`, which has no main window and `ShutdownMode.OnExplicitShutdown`.

On start-up `KumoApplication`:

1. loads settings (corrupt files are set aside as `settings.json.corrupt-<timestamp>`),
2. switches WPF to software rendering (see [performance](performance.md)),
3. creates one hidden `MessageWindow` that hosts the tray icon (`Shell_NotifyIcon`, re-added on `TaskbarCreated`)
   and the global shortcut (`RegisterHotKey`),
4. starts `GlobalDragMonitor` (low-level mouse hook on its own thread),
5. listens for hand-overs from later launches,
6. pre-builds the overlay at application-idle priority.

Exit disposes the overlay controller (which cancels running jobs and waits up to three seconds for them to clean
up their temporary files), the hook, the hotkey and the tray icon.

Kumo needs no service, no scheduled task and no elevation. Autostart is a per-user `Run` value written only when
the user enables it.

## Input path

`GlobalDragMonitor` owns a dedicated thread that does nothing but pump hook messages. Its callbacks feed
`DragGestureDetector`, a pure state machine, and post `InputEvent`s to the UI dispatcher. The keyboard hook exists
only while a shell drag is in progress. Details and the rationale are in [interaction.md](interaction.md).

## Overlay

| Type | Role |
| --- | --- |
| `OverlayController` | State machine: hidden → awaiting files → menu → running → result. Owns jobs and timers. |
| `RadialOverlayWindow` | Topmost, transparent, non-activating WPF window and OLE drop target; hosts the menu and status bar. |
| `RadialMenuView` | Lays out segments and the hub, hit-tests, handles arrow-key navigation, runs the short open transition. |
| `RadialSegment`, `RadialHub` | Template-less `Button`s that draw themselves and hit-test only their own shape, so they keep keyboard focus and UI Automation support without a visual tree per segment. |
| `RadialLayout` | Geometry in DIPs derived from the ring diameter. |
| `HoverDwell` | Spring-loaded hover: resting on a category opens it, resting on the hub goes back. |
| `KumoMascot`, `MascotArt` | The mascot as frozen vector drawings per state; the same data generates the SVG and ICO assets. |

`RadialMenuModel` (in Core) decides what the ring shows. Categories with a single entry are promoted to direct
actions, so for example "Merge PDFs" needs one step.

The OLE `Drop` handler only records the target and defers the action with `Dispatcher.BeginInvoke`: `Drop` is a
synchronous COM call from the drag source, and File Explorer is blocked until it returns.

## File operations

```
IFileOperation ──► OperationRegistry.EntriesFor(files) ──► MenuEntry ──► OperationExecutor.RunAsync ──► BatchResult
                                                                              │
                                                                              └─► IOutputWriter (atomic, collision-safe)
```

- `SignatureFileTypeResolver` reads the first kilobyte of each file and identifies PNG, JPEG, WEBP, GIF, BMP, TIFF
  and PDF by signature.
- An `IFileOperation` declares its category, label, whether it is combined (works on the whole selection) and
  whether it needs input (page range, password). `Supports(file)` decides applicability per file.
- `OperationRegistry` offers an entry only when every selected file is supported. Operations with the same
  category and label are grouped into one `MenuEntry`, and each file is routed to the first operation that
  supports it, which is how "Convert → PNG" serves both images and PDFs.
- `OperationExecutor` runs per-file entries with bounded concurrency (`SemaphoreSlim`, 1–4) on thread-pool threads
  and combined entries once. Every exception is classified into an `OperationErrorKind` with a message meant for
  people; unexpected exceptions are logged with their stack trace but never shown.
- `OutputWriter` writes to a hidden `.kumo-<guid>.tmp` in the destination folder, checks free space first, and
  renames into place without overwriting. Sources and files written in the same batch are never replaced, even in
  "replace" mode.

### Libraries

| Need | Choice | Why |
| --- | --- | --- |
| Image decode/encode, resize | SkiaSharp 3.119 (MIT) | Mature, maintained, encodes WEBP (WIC cannot), exposes header dimensions before decoding. About 11 MB native. |
| BMP output | Own 24-bit writer | Skia does not encode BMP; the format is trivial. |
| PDF merge/split/extract/protect, images to PDF | PDFsharp 6.2.4 (MIT) | Pure managed. **6.5.0 and later are AGPL-3.0**, so the version is pinned. |
| PDF to image | `Windows.Data.Pdf` | Built into Windows, no extra renderer to ship. Costs the 25 MB C#/WinRT projection assembly on disk, loaded only when used. |

PDFsharp 6.2.4 depends on `System.Security.Cryptography.Pkcs` 8.0.1, which has published advisories; the
project references 10.0.13 explicitly so the patched version is used.

## Threading

- UI thread: WPF dispatcher, overlay, tray and hotkey message window.
- Hook thread: low-level hooks only.
- Thread pool: file inspection when files are dropped and all operation work.
- A short-lived STA thread reads the Explorer selection through COM, with a five-second timeout so a busy Explorer
  cannot freeze Kumo.

## Testing

| Project | Covers |
| --- | --- |
| `Kumo.Core.Tests` | Type detection, registry filtering and grouping, executor (failures, cancellation, concurrency bound, size limits, recycling), output naming and atomic writes, settings persistence, hotkey parsing, page ranges, radial geometry and menu transitions, summaries. |
| `Kumo.Operations.Tests` | Real conversions on generated fixtures: formats, transparency, EXIF orientation, resizing, compression, rotation, corrupt files, decompression bombs, locked files, PDF merge/split/extract/protect/render, encrypted inputs. |
| `Kumo.Windows.Tests` | Gesture rules, shell window classes, single-instance hand-over, autostart command line. |
| `tools/Kumo.DragProbe` | End-to-end checks with real input against a running Kumo: Explorer drag → conversion, Escape, release elsewhere, held Shift, keyboard shortcut. |
