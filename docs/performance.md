# Performance

## Targets

| Target | Budget |
| --- | --- |
| Idle CPU | about 0 % |
| Idle memory | 60 MB or less private working set |
| Background work | no polling, no periodic timers, no network, no continuous animation |
| Overlay | appears quickly; UI stays responsive during conversions |

## Measurements

Release build, framework-dependent, `win-x64`, Windows 11 (build 26200), measured on 2026-10-19. Private working
set comes from `Win32_PerfFormattedData_PerfProc_Process.WorkingSetPrivate`; private bytes from
`Process.PrivateMemorySize64`; CPU from `Process.TotalProcessorTime`. Menus were opened through the single-instance
hand-over and driven with UI Automation, which itself makes the target process allocate automation peers, so the
"after use" figures are slightly pessimistic.

| Situation | Private working set | Private bytes |
| --- | --- | --- |
| Idle, 10 s after launch (overlay pre-built) | 15.1 MB | 38.9 MB |
| After opening the menu three times | 31.9 MB | 52.1 MB |
| After converting a 12 MP JPG to WEBP, 30 s later | 30.1 MB | 49.9 MB |
| After 13 open/navigate/close cycles | 41.8 MB | 62.1 MB |

| Metric | Result |
| --- | --- |
| CPU time while idle for 60 s | 0 ms and 31 ms in two runs (the second with the pointer being moved, which runs the mouse hook) |
| Process ready for input after launch | 175–191 ms |
| Menu shown (from request to visible) | about 95 ms the first time, 9–14 ms afterwards |
| 12 MP JPG → WEBP | 0.50 s (from the executor's start and finish log entries) |
| Publish size | 38 MB (25 MB of it is the Windows SDK projection used for PDF rendering) |

Repeated use did not show growth beyond a plateau: handle counts stayed flat (709–718 over 12 cycles) and memory
oscillated rather than climbing.

## Decisions and trade-offs

**Event-driven input.** A low-level mouse hook replaces a polling timer. The callback is a few comparisons when no
shell drag is in progress, and the thread sleeps in `GetMessage` otherwise.

**Software rendering.** `RenderOptions.ProcessRenderMode = SoftwareOnly`. With hardware rendering the first
overlay created a Direct3D device whose buffers stayed committed: after 13 menu cycles private bytes were about
145 MB with hardware rendering and 62 MB with software rendering. The overlay is small and static, so drawing it in
software costs nothing noticeable.

**Pre-building the overlay.** Creating the first WPF window takes about 400 ms (framework resources, fonts, the
layered window). Kumo builds the overlay once at application-idle priority after start-up, which moves the first
menu from about 400 ms to about 95 ms and raises idle private working set from about 8 MB to about 15 MB.
ReadyToRun compilation was tried and did not help, because the cost is framework initialisation rather than JIT.

**Releasing memory after work.** Decoded images live on the large-object heap. After the last running job finishes,
Kumo performs one compacting collection so memory returns close to its pre-job level instead of waiting for the next
natural GC.

**Bounded work.** At most four files are processed at once (default two). Input files over 1 GB, images over 200
megapixels and PDFs over 5,000 pages are refused before any large allocation.

**No idle timers.** All `DispatcherTimer`s (hover dwell, drop grace period, result auto-hide) run only while the
overlay is visible. The single-instance pipe waits on an I/O completion, not a loop. No file system watchers.

## Reproducing

```powershell
dotnet publish src/Kumo.App -c Release -r win-x64 --self-contained false -o artifacts/publish
```

Start `artifacts/publish/Kumo.exe --background`, wait, then sample:

```powershell
$p = Get-Process Kumo
Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object IDProcess -eq $p.Id |
  Select-Object @{n='PrivateWorkingSetMB';e={$_.WorkingSetPrivate/1MB}}
$p.TotalProcessorTime
```

The overlay logs `"ms"` with each "Overlay activated" and "Menu opened" entry in `%LOCALAPPDATA%\Kumo\logs`.

## Not measured yet

- Behaviour on low-end hardware and on Windows 10.
- Large batches (hundreds of files) and very large PDFs.
- Memory after days of uptime.
