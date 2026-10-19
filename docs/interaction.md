# Interaction model and drag-and-drop feasibility

This document records how Kumo integrates with Windows drag-and-drop, which alternatives were evaluated,
and what has and has not been verified.

## Constraints

- A WPF application cannot observe another process's OLE drag-and-drop session. `DoDragDrop` runs a modal
  loop inside the source process (Explorer); the data object is only handed to whichever registered
  `IDropTarget` is under the pointer.
- A low-level mouse hook sees pointer and button events, not the dragged data. The file list must therefore
  come from a real drop onto a window that Kumo owns.
- Explorer already gives modifiers a meaning during a drag: Shift forces *move*, Ctrl forces *copy*,
  Ctrl+Shift or Alt creates a shortcut. Kumo must not change what happens when the user drops somewhere else.
- No code is injected into Explorer, and no shell extension is loaded into it.

## Chosen design

1. **Gesture detection (global, event-driven).** `GlobalDragMonitor` installs a `WH_MOUSE_LL` hook on a
   dedicated thread that only pumps hook messages. It never blocks or swallows input; every event is passed
   on with `CallNextHookEx`. A press is only followed if it lands on a shell surface: the root window under
   the pointer has class `CabinetWClass`/`ExploreWClass` (File Explorer) or `Progman`/`WorkerW` (desktop).
   Once the pointer moves past the system drag threshold (`SM_CXDRAG`/`SM_CYDRAG`) the drag is considered
   started.
2. **Keyboard observation only during a drag.** A `WH_KEYBOARD_LL` hook is installed when such a drag starts
   and removed when the button is released, so keystrokes are never observed outside a file drag. Only
   Shift, Escape and "another key was pressed" are evaluated; nothing is recorded.
3. **Activation.** By default Kumo opens on a *Shift tap* (press and release within 450 ms while dragging).
   Holding Shift through the drop keeps Explorer's force-move behaviour untouched. A *Shift hold* mode,
   closer to ZestDrop's behaviour, is available in settings; it opens as soon as Shift is held during a drag.
   Ctrl+Shift and Alt combinations are ignored because Explorer uses them.
4. **Real OLE drop target.** The radial overlay is a WPF window with `AllowDrop`, which registers an
   `IDropTarget` through `RegisterDragDrop`. It is created with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and
   topmost, so Explorer keeps focus and the drag continues uninterrupted. It reads `CF_HDROP`
   (`DataFormats.FileDrop`) and answers `DROPEFFECT_COPY` only over an operation, so the drag source never
   deletes the original (which a *move* effect would allow).
5. **Placement and DPI.** The process is Per-Monitor V2 DPI aware. The overlay is first moved (not resized)
   onto the monitor under the pointer so WPF processes the DPI change, then centred on the pointer and clamped
   to that monitor's work area using physical pixels from `GetMonitorInfo`.
6. **Dismissal.** The hook reports the button release before Explorer delivers `IDropTarget::Drop` through
   COM, so the overlay waits 300 ms for a pending drop before hiding. Escape (which Explorer also uses to
   cancel the drag) hides it immediately.

### Alternatives considered

| Option | Outcome |
| --- | --- |
| Polling `GetAsyncKeyState` on a timer (ZestDrop's approach) | Rejected: wakes the CPU 40 times per second forever. |
| Shell extension / injection into Explorer | Rejected: unsafe, fragile, and violates the no-injection rule. |
| Reading the drag payload from the hook | Impossible: the hook has no access to the data object. |
| Always-visible edge drop target | Kept as a possible future activation method; it costs screen space and is less discoverable. |
| Shift hold only | Conflicts with Explorer's force-move; offered as an opt-in mode. |

### Keyboard-only path

Radial gestures are not the only entry point. Kumo also offers a global shortcut (registered with
`RegisterHotKey`) and the tray menu, both of which open the same radial menu in click/keyboard mode.
These are alternatives, not equivalents of drag interception.

## Verification

### Automated, real input (`tools/Kumo.DragProbe`)

`Kumo.DragProbe` opens a File Explorer window on a sample file and drives real drags with `SendInput`,
locating items through UI Automation, then checks Kumo's log and the file system. Because the input is
synthesized at the OS level, Explorer performs a genuine `DoDragDrop` and Kumo receives a genuine OLE drop.
It takes over the pointer for a few seconds and aborts if the pointer is moved by hand.

Results on Windows 11 Pro 10.0.26200 (single monitor), 2026-10-19:

| Scenario | Result |
| --- | --- |
| Drag from Explorer, tap Shift, drop on the top segment | PASS: overlay shown, drop received with the file list and the correct segment; source file intact |
| Tap Shift, press Escape | PASS: overlay dismissed, no drop, source intact |
| Tap Shift, release over empty Explorer space | PASS: overlay dismissed after release, source intact |
| Hold Shift for 700 ms during the drag (Explorer force-move) | PASS: overlay stays closed |

Run it with:

```powershell
dotnet build Kumo.slnx
tools/Kumo.DragProbe/bin/Debug/net10.0-windows/Kumo.DragProbe.exe `
  src/Kumo.App/bin/Debug/net10.0-windows10.0.19041.0/Kumo.exe C:\path\to\kumo-probe\sample.png
```

### Unit tests

`DragGestureDetector` holds all activation rules without Win32 calls and is covered by
`Kumo.Windows.Tests` (tap timing, auto-repeat, held Shift, Ctrl/Alt combinations, non-shell sources,
hold mode, Escape, reset on release).

### Not yet verified (manual validation required)

- Drags from the desktop (only Explorer windows were driven by the probe).
- Multiple monitors with different scale factors; 125/150/200 % scaling.
- Explorer restart while Kumo is running (the hook is process-independent, but the tray icon must be re-added).
- Drags from elevated windows. User Interface Privilege Isolation blocks drops from an elevated process into
  a non-elevated one; Kumo does not request elevation, so such drags are expected not to work.
- Pen and touch drags.

## Known limitations

- Activation requires a drag that starts on File Explorer or the desktop. Drags started in other
  applications (browsers, mail clients) do not open Kumo through the gesture.
- While the overlay is open it covers the area around the pointer. Dropping elsewhere still works normally,
  but a folder hidden under the overlay cannot be targeted until the pointer leaves the overlay area.
- Remote Desktop and some virtual machines may deliver modifier keys late, which can make a tap exceed the
  450 ms window.
