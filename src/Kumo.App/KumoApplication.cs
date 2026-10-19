using System.IO;
using System.Windows;
using System.Windows.Threading;
using Kumo.App.Overlay;
using Kumo.Core.Diagnostics;
using Kumo.Windows.Input;

namespace Kumo.App;

internal sealed class KumoApplication : Application
{
    private readonly Log _log = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kumo", "logs"));
    private GlobalDragMonitor? _monitor;
    private RadialOverlayWindow? _overlay;

    public KumoApplication()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _monitor = new GlobalDragMonitor(
            input => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => OnInput(input)),
            ex => _log.Error("Input hook fault", ex));
        _monitor.Start();
        _log.Info("Kumo started", ("pid", Environment.ProcessId));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        _log.Info("Kumo stopped");
        base.OnExit(e);
    }

    private void OnInput(InputEvent input)
    {
        switch (input.Signal)
        {
            case InputSignal.Activate:
                _overlay ??= CreateOverlay();
                _overlay.ShowAt(input.Position);
                _log.Info("Overlay activated", ("x", input.Position.X), ("y", input.Position.Y));
                break;

            case InputSignal.DragEnded:
                // The hook sees the button release before Explorer delivers IDropTarget::Drop through COM,
                // so dismissal waits briefly to let a pending drop arrive first.
                DismissLater();
                break;

            case InputSignal.Escape:
                _overlay?.Hide();
                _log.Info("Overlay dismissed", ("reason", "escape"));
                break;
        }
    }

    private RadialOverlayWindow CreateOverlay()
    {
        var overlay = new RadialOverlayWindow();
        overlay.FilesDropped += (files, action) =>
        {
            _log.Info("Drop accepted", ("action", action), ("files", files.Count));
            overlay.Hide();
        };
        return overlay;
    }

    private void DismissLater()
    {
        if (_overlay is not { IsVisible: true } overlay)
            return;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!overlay.DropReceived && overlay.IsVisible)
            {
                overlay.Hide();
                _log.Info("Overlay dismissed", ("reason", "released-elsewhere"));
            }
        };
        timer.Start();
    }
}
