using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Kumo.App.Mascot;
using Kumo.App.Theming;
using Kumo.App.Windows;
using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Core.Radial;
using Kumo.Core.Settings;
using Kumo.Windows.Input;
using Kumo.Windows.Interop;
using Kumo.Windows.Shell;

namespace Kumo.App.Overlay;

/// <summary>
/// Drives the overlay through its phases: waiting for the dragged files, offering actions, running a job and
/// showing the result. Jobs outlive the overlay: if it is dismissed while working, the result arrives as a
/// notification instead.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private enum Phase
    {
        Hidden,
        AwaitingFiles,
        Menu,
        Running,
        Result,
    }

    private sealed class Job(MenuEntry entry, IReadOnlyList<InputFile> files)
    {
        public MenuEntry Entry { get; } = entry;

        public IReadOnlyList<InputFile> Files { get; } = files;

        public CancellationTokenSource Cancel { get; } = new();

        public Task? Completion { get; set; }

        public bool Attached { get; set; } = true;
    }

    public const int MaxFiles = 500;

    private static readonly TimeSpan CategoryDwell = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan BackDwell = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DropGrace = TimeSpan.FromMilliseconds(300);

    private readonly KumoApplication _app;
    private readonly IFileOperationRegistry _registry;
    private readonly IOperationExecutor _executor;
    private readonly IFileTypeResolver _resolver;
    private readonly HoverDwell _dwell = new();
    private readonly DispatcherTimer _dismissTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _resultTimer = new(DispatcherPriority.Background);
    private readonly List<Job> _jobs = [];

    private RadialOverlayWindow? _window;
    private Phase _phase;
    private IReadOnlyList<InputFile> _files = [];
    private RadialMenuModel? _model;
    private bool _filesFromCurrentDrag;
    private bool _dropAccepted;
    private int _loadGeneration;
    private Job? _attachedJob;

    public OverlayController(KumoApplication app, IFileOperationRegistry registry, IOperationExecutor executor, IFileTypeResolver resolver)
    {
        _app = app;
        _registry = registry;
        _executor = executor;
        _resolver = resolver;
        _dismissTimer.Tick += (_, _) => OnDropGraceElapsed();
        _resultTimer.Tick += (_, _) => OnResultTimeout();
    }

    public bool HasRunningJobs => _jobs.Count > 0;

    public string? LastOutput { get; private set; }

    public void OnInput(InputEvent input)
    {
        switch (input.Signal)
        {
            case InputSignal.Activate:
                OpenForDrag(input.Position);
                break;

            case InputSignal.DragEnded:
                if (_phase is Phase.AwaitingFiles or Phase.Menu && _window is { IsInteractive: false })
                {
                    _dismissTimer.Interval = DropGrace;
                    _dismissTimer.Start();
                }
                break;

            case InputSignal.Escape:
                if (_phase is Phase.AwaitingFiles or Phase.Menu && _window is { IsInteractive: false })
                    Hide("escape");
                break;

            case InputSignal.PointerDown:
                if (_phase != Phase.Hidden && _window != null && !_window.ContainsScreenPoint(input.Position))
                    Hide("outside-click");
                break;
        }
    }

    public void OpenForFiles(IReadOnlyList<string> paths, ScreenPoint point, bool fromKeyboard)
    {
        long started = Stopwatch.GetTimestamp();
        var window = Prepare(point);
        _phase = Phase.Menu;
        window.SetStatus("Reading files…");
        window.ShowAt(point);
        EnterClickMode();
        BeginLoading(paths, focusFirst: fromKeyboard);
        _app.Log.Info("Menu opened", ("files", paths.Count), ("source", fromKeyboard ? "keyboard" : "pointer"),
            ("ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }

    public async void OpenForSelection()
    {
        try
        {
            var files = await ExplorerSelection.GetForForegroundAsync(TimeSpan.FromSeconds(5));
            if (files.Count == 0)
            {
                _app.Log.Info("Shortcut used without a selection");
                _app.Notify("Nothing selected", "Select files in File Explorer or on the desktop, then press the shortcut again.", TrayNotificationKind.Info);
                return;
            }
            OpenForFiles(files, OverlayWindowInterop.GetCursorPosition(), fromKeyboard: true);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _app.Log.Error("Reading the Explorer selection failed", ex);
        }
    }

    /// <summary>
    /// Builds the overlay once, off screen, after start-up. Creating the first WPF window costs several hundred
    /// milliseconds; paying it in advance keeps the first gesture as quick as later ones.
    /// </summary>
    public void Prewarm()
    {
        if (_window != null)
            return;
        var window = Prepare(OverlayWindowInterop.GetCursorPosition());
        window.Menu.ShowItems([new RadialItem("Kumo", "Kumo", RadialItemKind.Action, OperationCategory.Convert, null)], showIcons: true, animate: false);
        window.SetStatus("Kumo");
        window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        window.Arrange(new Rect(window.DesiredSize));
        window.UpdateLayout();
        window.Menu.ClearItems();
    }

    public void Dispose()
    {
        foreach (var job in _jobs.ToList())
            job.Cancel.Cancel();
        var pending = _jobs.Select(j => j.Completion).OfType<Task>().ToArray();
        if (pending.Length > 0)
            Task.WaitAny(Task.WhenAll(pending), Task.Delay(TimeSpan.FromSeconds(3)));
        _dwell.Cancel();
        _dismissTimer.Stop();
        _resultTimer.Stop();
        _window?.Close();
        _window = null;
    }

    private RadialOverlayWindow Prepare(ScreenPoint point)
    {
        DetachJob();
        StopTimers();
        var settings = _app.Settings;
        var palette = Theme.Resolve(settings.Theme);
        double scale = OverlayWindowInterop.ScaleAt(point);
        var work = OverlayWindowInterop.GetWorkArea(point);
        double preferred = settings.MenuSize switch
        {
            MenuSize.Compact => 280,
            MenuSize.Large => 410,
            _ => 340,
        };
        var probe = new RadialLayout(preferred);
        double available = work.Height / scale - probe.StatusHeight * 2 - probe.Margin * 2 - 16;
        var layout = new RadialLayout(Math.Clamp(preferred, 220, Math.Max(220, available)));

        if (_window == null)
        {
            _window = new RadialOverlayWindow(layout, palette);
            Attach(_window);
        }
        else
        {
            _window.ApplyLayout(layout, palette);
        }

        _window.Menu.ReducedMotion = Theme.ReducedMotion(settings.Motion);
        _window.Menu.ClearItems();
        _window.Menu.SetRingVisible(true);
        _window.Menu.Hub.ShowsBack = false;
        _window.Menu.Hub.Progress = null;
        _window.Menu.Hub.MascotState = MascotState.Dragging;
        _window.SetInteractive(false);
        _files = [];
        _model = null;
        _filesFromCurrentDrag = false;
        _dropAccepted = false;
        return _window;
    }

    private void Attach(RadialOverlayWindow window)
    {
        window.DragEnter += OnDragEnter;
        window.DragOver += OnDragOver;
        window.DragLeave += OnDragLeave;
        window.Drop += OnDrop;
        window.KeyDown += OnKeyDown;
        window.Menu.SegmentInvoked += index => Invoke(index, fromDrop: false);
        window.Menu.HubInvoked += OnHubInvoked;
        window.Menu.BackRequested += () => GoBack();
        window.Menu.HighlightChanged += OnHighlightChanged;
        window.MouseEnter += (_, _) => _resultTimer.Stop();
        window.MouseLeave += (_, _) =>
        {
            if (_phase == Phase.Result)
                _resultTimer.Start();
        };
    }

    private void OpenForDrag(ScreenPoint point)
    {
        long started = Stopwatch.GetTimestamp();
        var window = Prepare(point);
        _phase = Phase.AwaitingFiles;
        window.SetStatus("Reading files…");
        window.ShowAt(point);
        _app.SetPointerTracking(false);
        _app.Log.Info("Overlay activated", ("x", point.X), ("y", point.Y), ("ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _app.Log.Debug("Drag entered overlay", ("phase", _phase.ToString()), ("filesKnown", _filesFromCurrentDrag));
        _dismissTimer.Stop();
        if (!_filesFromCurrentDrag && _phase is Phase.AwaitingFiles or Phase.Menu)
        {
            _filesFromCurrentDrag = true;
            BeginLoading(ReadDroppedPaths(e.Data), focusFirst: false);
        }
        OnDragOver(sender, e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (_window == null || _phase != Phase.Menu || _model is not { IsEmpty: false })
            return;

        var hit = _window.Menu.Highlight(_window.Menu.HitTest(e.GetPosition(_window.Menu)));
        _window.Menu.Hub.MascotState = hit.Kind == RadialHitKind.Segment ? MascotState.Hovering : MascotState.Dragging;
        if (hit.Kind == RadialHitKind.Outside)
        {
            _dwell.Cancel();
            return;
        }

        e.Effects = DragDropEffects.Copy;
        if (hit.Kind == RadialHitKind.Segment && _model.Items[hit.Index].OpensSubmenu)
            _dwell.Start(hit, CategoryDwell, () => Invoke(hit.Index, fromDrop: false));
        else if (hit.Kind == RadialHitKind.Hub && !_model.IsAtRoot)
            _dwell.Start(hit, BackDwell, () => GoBack());
        else
            _dwell.Cancel();
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _dwell.Cancel();
        _window?.Menu.Highlight(RadialHit.Outside);
        if (_window != null && _phase == Phase.Menu)
            _window.Menu.Hub.MascotState = MascotState.Dragging;
        e.Handled = true;
    }

    // Drop is a synchronous COM call from the drag source: Explorer waits until it returns, so any real work,
    // and above all any dialog, is deferred to after the drop completes.
    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        _dwell.Cancel();
        if (_window == null || _phase != Phase.Menu || _model is not { IsEmpty: false })
        {
            _app.Log.Debug("Drop rejected", ("phase", _phase.ToString()), ("menuReady", _model is { IsEmpty: false }));
            return;
        }

        var hit = _window.Menu.HitTest(e.GetPosition(_window.Menu));
        if (hit.Kind == RadialHitKind.Outside)
            return;

        _dropAccepted = true;
        _dismissTimer.Stop();
        e.Effects = DragDropEffects.Copy;
        _app.Log.Info("Drop accepted", ("files", _files.Count), ("target", hit.Kind.ToString()));
        if (hit.Kind == RadialHitKind.Segment)
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Invoke(hit.Index, fromDrop: true));
        else
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, EnterClickMode);
    }

    private void OnDropGraceElapsed()
    {
        _dismissTimer.Stop();
        if (!_dropAccepted && _phase is Phase.AwaitingFiles or Phase.Menu && _window is { IsInteractive: false })
            Hide("released-elsewhere");
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        if (_phase == Phase.Menu && GoBack())
            return;
        Hide("escape");
    }

    private void OnHubInvoked()
    {
        if (_phase == Phase.Menu && GoBack())
            return;
        if (_phase is Phase.Menu or Phase.Result or Phase.AwaitingFiles)
            Hide("closed");
    }

    private void OnHighlightChanged(RadialItem? item)
    {
        if (_window == null || _phase != Phase.Menu)
            return;
        _window.SetStatus(item?.Description ?? MenuStatus());
    }

    private string[] ReadDroppedPaths(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] ?? [] : [];
        }
        catch (COMException ex)
        {
            _app.Log.Warning("Dropped data could not be read", ("detail", ex.Message));
            return [];
        }
    }

    private async void BeginLoading(IReadOnlyList<string> paths, bool focusFirst)
    {
        int generation = ++_loadGeneration;
        _phase = Phase.Menu;
        _model = null;
        var window = _window!;

        if (paths.Count > MaxFiles)
        {
            ShowUnavailable($"Kumo works with up to {MaxFiles} files at a time.");
            return;
        }

        IReadOnlyList<InputFile> files;
        bool hadFolders;
        try
        {
            (files, hadFolders) = await Task.Run(() =>
            {
                var resolved = paths.Where(File.Exists).Select(_resolver.Resolve).ToList();
                return ((IReadOnlyList<InputFile>)resolved, paths.Any(Directory.Exists));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Log.Warning("Files could not be inspected", ("detail", ex.Message));
            files = [];
            hadFolders = false;
        }

        if (generation != _loadGeneration || _phase != Phase.Menu || !window.IsVisible)
            return;

        _files = files;
        _model = new RadialMenuModel(_registry.EntriesFor(files));
        _app.Log.Debug("Files inspected", ("files", files.Count), ("kinds", string.Join(",", files.Select(f => f.Kind).Distinct())));
        if (_model.IsEmpty)
        {
            ShowUnavailable(Summaries.Unavailable(files, hadFolders));
            return;
        }

        window.Menu.ShowItems(_model.Items, showIcons: true, animate: true);
        window.Menu.Hub.MascotState = window.IsInteractive ? MascotState.Idle : MascotState.Dragging;
        window.SetStatus(MenuStatus());
        if (focusFirst)
            window.Menu.FocusSegment(0);
    }

    private void ShowUnavailable(string message)
    {
        if (_window == null)
            return;
        _model = new RadialMenuModel([]);
        _window.Menu.ClearItems();
        _window.Menu.Hub.MascotState = MascotState.Cancelled;
        _window.SetStatus(message, new StatusAction("Close", () => Hide("closed")));
        _app.Log.Info("No actions available", ("reason", message));
    }

    private string MenuStatus()
    {
        if (_model is { IsAtRoot: false })
            return _model.Title + " — " + Summaries.Selection(_files);
        return Summaries.Selection(_files);
    }

    private void Invoke(int index, bool fromDrop)
    {
        if (_model == null || _window == null || _phase != Phase.Menu)
            return;
        _dwell.Cancel();
        var activation = _model.Activate(index);
        switch (activation.Kind)
        {
            case RadialActivationKind.OpenedSubmenu:
                _window.Menu.ShowItems(_model.Items, showIcons: false, animate: true);
                _window.Menu.Hub.ShowsBack = true;
                _window.SetStatus(MenuStatus());
                if (fromDrop)
                    EnterClickMode();
                if (_window.IsInteractive && Keyboard.FocusedElement is RadialSegment)
                    _window.Menu.FocusSegment(0);
                break;

            case RadialActivationKind.Execute:
                StartJob(activation.Entry!);
                break;
        }
    }

    private bool GoBack()
    {
        if (_model == null || _window == null || !_model.Back())
            return false;
        _dwell.Cancel();
        _window.Menu.ShowItems(_model.Items, showIcons: true, animate: true);
        _window.Menu.Hub.ShowsBack = false;
        _window.SetStatus(MenuStatus());
        if (_window.IsInteractive && Keyboard.FocusedElement is RadialSegment)
            _window.Menu.FocusSegment(0);
        return true;
    }

    private void EnterClickMode()
    {
        if (_window == null || _phase == Phase.Hidden)
            return;
        bool foreground = _window.SetInteractive(true);
        _window.Menu.Hub.MascotState = MascotState.Idle;
        _app.SetPointerTracking(true);
        _app.Log.Debug("Menu became interactive", ("foreground", foreground));
    }

    private async void StartJob(MenuEntry entry)
    {
        if (_window == null)
            return;
        var window = _window;
        var files = _files;
        var options = AskForInput(entry, files.Count);
        if (options == null)
        {
            EnterClickMode();
            return;
        }

        var settings = _app.Settings;
        var job = new Job(entry, files);
        _jobs.Add(job);
        _attachedJob = job;
        _phase = Phase.Running;
        _app.SetPointerTracking(true);

        window.Menu.ClearItems();
        window.Menu.SetRingVisible(false);
        window.Menu.Hub.ShowsBack = false;
        window.Menu.Hub.Progress = 0;
        window.Menu.Hub.MascotState = entry.Category switch
        {
            OperationCategory.Convert => MascotState.Converting,
            OperationCategory.Compress => MascotState.Compressing,
            _ => MascotState.Processing,
        };
        window.SetStatus(Summaries.Progress(entry, new BatchProgress(0, entry.IsCombined ? 1 : files.Count, null, 0)),
            new StatusAction("Cancel", () => job.Cancel.Cancel()));

        var progress = new Progress<BatchProgress>(p =>
        {
            if (!job.Attached || _window == null)
                return;
            _window.Menu.Hub.Progress = p.Fraction;
            _window.SetStatus(Summaries.Progress(entry, p), new StatusAction("Cancel", () => job.Cancel.Cancel()));
        });

        var writer = new OutputWriter(settings.OutputDirectory, settings.Collisions, files.Select(f => f.Path));
        var policy = new ExecutionPolicy(settings.MaxConcurrentOperations, settings.PreserveOriginals);
        var run = _executor.RunAsync(entry, files, options, writer, policy, progress, job.Cancel.Token);
        job.Completion = run;
        BatchResult result;
        try
        {
            result = await run;
        }
        finally
        {
            _jobs.Remove(job);
            job.Cancel.Dispose();
        }
        ShowResult(job, result);
        ReleaseMemoryWhenIdle();
    }

    private OperationOptions? AskForInput(MenuEntry entry, int fileCount)
    {
        if (entry.Requirement == InputRequirement.None || _window == null)
            return OperationOptions.None;

        var theme = Theme.WindowThemeMode(_app.Settings.Theme);
        _app.SetPointerTracking(false);
        try
        {
            return entry.Requirement switch
            {
                InputRequirement.PageRange => InputDialog.AskPageRange(_window, theme, fileCount) is { } range ? new OperationOptions(PageRange: range) : null,
                InputRequirement.Password => InputDialog.AskPassword(_window, theme, Kumo.Operations.KumoOperations.MinimumPasswordLength) is { } password ? new OperationOptions(Password: password) : null,
                _ => OperationOptions.None,
            };
        }
        finally
        {
            _app.SetPointerTracking(_phase != Phase.Hidden);
        }
    }

    private void ShowResult(Job job, BatchResult result)
    {
        var summary = Summaries.Describe(result);
        LastOutput = result.Outputs.FirstOrDefault() ?? LastOutput;
        string? firstOutput = result.Outputs.FirstOrDefault();

        if (!job.Attached || _window == null || !_window.IsVisible)
        {
            var kind = summary.Tone switch
            {
                ResultTone.Failure or ResultTone.Partial => TrayNotificationKind.Error,
                _ => TrayNotificationKind.Info,
            };
            _app.Notify(summary.Headline, summary.Detail ?? job.Entry.Description, kind);
            return;
        }

        _attachedJob = null;
        _phase = Phase.Result;
        var hub = _window.Menu.Hub;
        hub.Progress = null;
        hub.MascotState = summary.Tone switch
        {
            ResultTone.Success => MascotState.Success,
            ResultTone.Cancelled => MascotState.Cancelled,
            ResultTone.Nothing => MascotState.Idle,
            _ => MascotState.Error,
        };

        string text = summary.Detail is { Length: > 0 } detail && summary.Tone != ResultTone.Success ? $"{summary.Headline}. {detail}" : summary.Headline;
        var actions = new List<StatusAction>();
        if (firstOutput != null)
            actions.Add(new StatusAction("Show", () => Reveal(firstOutput)));
        actions.Add(new StatusAction("Close", () => Hide("closed")));
        _window.SetStatus(text, [.. actions]);
        _window.ToolTip = text;

        _resultTimer.Interval = summary.Tone switch
        {
            ResultTone.Success => TimeSpan.FromSeconds(2.6),
            ResultTone.Nothing or ResultTone.Cancelled => TimeSpan.FromSeconds(4),
            _ => TimeSpan.FromSeconds(8),
        };
        if (!_window.IsMouseOver)
            _resultTimer.Start();
    }

    private void OnResultTimeout()
    {
        _resultTimer.Stop();
        if (_phase == Phase.Result)
            Hide("result-shown");
    }

    public static void Reveal(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void Hide(string reason)
    {
        StopTimers();
        DetachJob();
        if (_window != null)
        {
            _window.ToolTip = null;
            _window.SetInteractive(false);
            _window.Menu.Hub.Progress = null;
            _window.Hide();
        }
        _phase = Phase.Hidden;
        _files = [];
        _model = null;
        _filesFromCurrentDrag = false;
        _app.SetPointerTracking(false);
        _app.Log.Info("Overlay dismissed", ("reason", reason));
    }

    private void DetachJob()
    {
        if (_attachedJob != null)
        {
            _attachedJob.Attached = false;
            _attachedJob = null;
        }
    }

    private void StopTimers()
    {
        _dwell.Cancel();
        _dismissTimer.Stop();
        _resultTimer.Stop();
    }

    // Image work leaves large decoded buffers on the large-object heap. Compacting once after the last job keeps
    // Kumo's idle footprint close to what it was before the work started.
    private void ReleaseMemoryWhenIdle()
    {
        if (_jobs.Count > 0)
            return;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
