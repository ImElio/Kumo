# Roadmap

## Next engineering priorities

1. **Finish manual validation** ([checklist](manual-validation.md)): desktop drags, multi-monitor and mixed DPI,
   Explorer restart, sleep/resume, Narrator, Windows 10.
2. **Investigate the one unaccepted drop** seen by the drag probe (first drag after a Release start-up). Detailed
   logging now records drag entry and rejected drops.
3. **Packaging.** Recommended order:
   - The per-user NSIS installer is built; it needs code signing (next item) before Kumo can update through it.
   - MSIX later. It would need the `windows.startupTask` extension instead of the `Run` key and a Store-signed
     identity; low-level hooks and drag-and-drop work in full-trust MSIX apps.
   - Releases are self-contained, so no runtime has to be installed; the installer is about 50 MB.
4. **Code signing** for the executable and installer, to avoid SmartScreen warnings and to enable verified
   self-updates (see [updater.md](updater.md)).
5. **Mascot artwork as true vectors.** The current artwork is raster images wrapped in SVG; real vector files would
   stay sharp at every size and shrink the assets.

## Features under consideration

| Feature | Notes |
| --- | --- |
| Crop | Needs a small editor window; must not become the default interaction. |
| More resize presets / custom size | A compact radial "size" state or a small dialog. |
| HEIC and TIFF input | TIFF via WIC; HEIC depends on the HEIF extension being installed. |
| PDF to text | Requires a text-extraction library; evaluate licenses (PdfPig is Apache-2.0). |
| PDF rotate pages, reorder | PDFsharp can do both. |
| Audio/video (extract audio, compress) | Would need FFmpeg; large binary and LGPL/GPL obligations. Only with a clear licensing plan. |
| Localization | Strings are currently in code; move to resources before adding languages. |
| Explorer context menu entry | Optional, per-user, using a static verb that launches `Kumo.exe` with the selection (the hand-over already exists). |
| Edge drop target | An alternative activation that needs no modifier key. |
| Per-category recents | Put the last used format first. |

## Non-goals

Accounts, cloud processing, telemetry, advertising, Electron or embedded browsers, Windows services, and anything
injected into File Explorer.
