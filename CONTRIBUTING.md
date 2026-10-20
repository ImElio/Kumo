# Contributing to Kumo

Thanks for helping improve Kumo. Keep changes focused on the Windows desktop app and its local file workflow.

## Development setup

- Windows 10 version 2004 or later, or Windows 11.
- .NET SDK 10.0.400 or later. `global.json` selects the SDK.
- Git. NSIS 3 is only needed to build release installers.

From the repository root, restore and build the solution, run the tests, then start the app:

```powershell
dotnet restore Kumo.slnx
dotnet format whitespace Kumo.slnx --verify-no-changes
dotnet build Kumo.slnx -c Release
dotnet test Kumo.slnx -c Release
dotnet run --project src/Kumo.App
```

CI runs version validation, formatting, build and tests on Windows. A local build has no update repository configured,
so it does not check for updates.

## Code map

- `src/Kumo.Core` contains shared models, settings, file detection and operation coordination.
- `src/Kumo.Operations` contains image and PDF operations.
- `src/Kumo.Windows` contains Windows shell and input integration.
- `src/Kumo.App` contains the WPF interface and application lifecycle.
- `tests` contains the Core, Operations and Windows test projects.
- `tools/Kumo.AssetGen` regenerates mascot images and icons from `assets/mascot`.
- `tools/Kumo.DragProbe` checks real Explorer drag flows.

The drag probe takes over the mouse and terminates any running Kumo process before it starts its own. Use it only in a
development session where that interruption is expected:

```powershell
dotnet run --project tools/Kumo.DragProbe -- <path-to-Kumo.exe> <sample-file> [scenario...]
```

## Project conventions

- Keep Windows UI and shell dependencies out of `Kumo.Core`.
- Keep file operations independent of WPF controls; add shared behavior to Core only when it is platform-neutral.
- Preserve originals by default and write results through the existing output pipeline.
- Add or update focused tests when changing behavior. Cover input validation, cancellation and output naming where they
  apply.
- Follow `.editorconfig`. Nullable reference types are enabled and compiler warnings fail the build.
- If changing mascot source art, run `dotnet run --project tools/Kumo.AssetGen -- .` and include its regenerated image,
  icon and preview assets.
- `Kumo.Operations` pins PDFsharp 6.2.4. Do not upgrade it past the MIT-licensed 6.2 line without a license review.

## Pull requests

Use a focused branch and pull request. Describe the user-visible change, relevant edge cases, and how you verified it.
Include tests for behavior changes and update the README when supported formats or user-facing behavior changes.
Do not include local publish folders, logs, personal files or unrelated formatting changes.
