using System.Windows.Threading;
using Kumo.Core.Radial;

namespace Kumo.App.Overlay;

/// <summary>
/// "Spring-loaded" hover: while a file is dragged, resting on a target for a moment performs its action, the way
/// File Explorer opens a folder when a drag pauses over it.
/// </summary>
internal sealed class HoverDwell
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Input);
    private RadialHit _target = RadialHit.Outside;
    private Action? _action;

    public HoverDwell()
    {
        _timer.Tick += (_, _) =>
        {
            var action = _action;
            Cancel();
            action?.Invoke();
        };
    }

    /// <summary>Restarts the countdown only when the target changes, so small pointer movements do not reset it.</summary>
    public void Start(RadialHit target, TimeSpan delay, Action action)
    {
        if (target == _target && _timer.IsEnabled)
            return;
        _target = target;
        _action = action;
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _target = RadialHit.Outside;
        _action = null;
    }
}
