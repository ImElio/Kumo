using System.Windows.Threading;
using Kumo.Windows.Input;
using Kumo.Windows.Interop;
using Kumo.Windows.Shell;

namespace Kumo.App.Overlay;

/// <summary>Routes gestures, shortcuts and hand-overs to the radial overlay.</summary>
internal sealed class MenuPresenter : IDisposable
{
    private readonly KumoApplication _app;
    private RadialOverlayWindow? _overlay;
    private DispatcherTimer? _dismissTimer;

    public MenuPresenter(KumoApplication app)
    {
        _app = app;
    }

    private RadialOverlayWindow Overlay => _overlay ??= CreateOverlay();

    public void OnInput(InputEvent input)
    {
        switch (input.Signal)
        {
            case InputSignal.Activate:
                Overlay.ShowAt(input.Position);
                _app.Log.Info("Overlay activated", ("x", input.Position.X), ("y", input.Position.Y));
                break;

            case InputSignal.DragEnded:
                DismissAfterPendingDrop();
                break;

            case InputSignal.Escape:
                Dismiss("escape");
                break;

            case InputSignal.PointerDown:
                if (_overlay is { IsVisible: true } overlay && !OverlayWindowInterop.GetBounds(overlay.Handle).Contains(input.Position))
                    Dismiss("outside-click");
                break;
        }
    }

    public void OpenForFiles(IReadOnlyList<string> files, ScreenPoint at)
    {
        Overlay.ShowForFiles(files, at);
        _app.SetPointerTracking(true);
        _app.Log.Info("Overlay opened for files", ("files", files.Count));
    }

    public async void OpenForSelection()
    {
        try
        {
            var files = await ExplorerSelection.GetForForegroundAsync(TimeSpan.FromSeconds(2));
            if (files.Count == 0)
            {
                _app.Notify("Nothing selected", "Select files in File Explorer or on the desktop, then press the shortcut again.", TrayNotificationKind.Info);
                return;
            }
            OpenForFiles(files, OverlayWindowInterop.GetCursorPosition());
        }
        catch (Exception ex)
        {
            _app.Log.Error("Reading the Explorer selection failed", ex);
        }
    }

    public void Dispose()
    {
        _dismissTimer?.Stop();
        _overlay?.Close();
        _overlay = null;
    }

    private RadialOverlayWindow CreateOverlay()
    {
        var overlay = new RadialOverlayWindow();
        overlay.FilesDropped += (files, action) =>
        {
            _app.Log.Info("Drop accepted", ("action", action), ("files", files.Count));
            Dismiss("selected");
        };
        overlay.IsVisibleChanged += (_, _) =>
        {
            if (!overlay.IsVisible)
                _app.SetPointerTracking(false);
        };
        return overlay;
    }

    private void Dismiss(string reason)
    {
        if (_overlay is not { IsVisible: true } overlay)
            return;
        overlay.Hide();
        _app.Log.Info("Overlay dismissed", ("reason", reason));
    }

    // The hook sees the button release before Explorer delivers IDropTarget::Drop through COM,
    // so dismissal waits briefly to let a pending drop arrive first.
    private void DismissAfterPendingDrop()
    {
        if (_overlay is not { IsVisible: true } overlay || overlay.IsHoldingFiles)
            return;
        _dismissTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _dismissTimer.Tick -= OnDismissTimer;
        _dismissTimer.Tick += OnDismissTimer;
        _dismissTimer.Start();
    }

    private void OnDismissTimer(object? sender, EventArgs e)
    {
        _dismissTimer?.Stop();
        if (_overlay is { DropReceived: false, IsHoldingFiles: false })
            Dismiss("released-elsewhere");
    }
}
