using Kumo.Windows.Input;

namespace Kumo.Windows.Tests;

public class DragGestureDetectorTests
{
    private static readonly ModifierState None = new(false, false, false);
    private static readonly ModifierState Shift = new(true, false, false);

    private static DragGestureDetector StartDrag(ActivationMode mode = ActivationMode.ShiftTap, bool shell = true, bool shiftAtStart = false)
    {
        var detector = new DragGestureDetector(4, 4) { Mode = mode };
        detector.OnButtonDown(new ScreenPoint(100, 100), shell, shiftAtStart);
        detector.OnMove(new ScreenPoint(120, 100), shiftAtStart ? Shift : None);
        return detector;
    }

    [Fact]
    public void Movement_beyond_threshold_starts_a_drag()
    {
        var detector = new DragGestureDetector(4, 4);
        detector.OnButtonDown(new ScreenPoint(10, 10), shellSurface: true, shiftDown: false);

        Assert.Equal(GestureSignal.None, detector.OnMove(new ScreenPoint(12, 11), None));
        Assert.Equal(GestureSignal.DragStarted, detector.OnMove(new ScreenPoint(15, 10), None));
        Assert.True(detector.IsDragging);
    }

    [Fact]
    public void Quick_shift_tap_during_drag_activates()
    {
        var detector = StartDrag();

        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, true, 1000, None));
        Assert.Equal(GestureSignal.Activate, detector.OnKey(VirtualKeys.LeftShift, false, 1120, None));
    }

    [Fact]
    public void Shift_held_longer_than_a_tap_keeps_explorer_move_semantics()
    {
        var detector = StartDrag();

        detector.OnKey(VirtualKeys.LeftShift, true, 1000, None);
        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1000 + DragGestureDetector.DefaultTapMilliseconds + 1, None));
    }

    [Fact]
    public void Shift_already_held_when_the_drag_began_does_not_count_as_a_tap()
    {
        var detector = StartDrag(shiftAtStart: true);

        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1050, None));
    }

    [Fact]
    public void Auto_repeat_does_not_restart_the_tap_timer()
    {
        var detector = StartDrag();

        detector.OnKey(VirtualKeys.LeftShift, true, 1000, None);
        detector.OnKey(VirtualKeys.LeftShift, true, 1400, None);
        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1600, None));
    }

    [Fact]
    public void Shift_combined_with_control_is_not_a_tap()
    {
        var detector = StartDrag();

        detector.OnKey(VirtualKeys.LeftShift, true, 1000, new ModifierState(false, true, false));
        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1080, None));
    }

    [Fact]
    public void Another_key_during_the_tap_spoils_it()
    {
        var detector = StartDrag();

        detector.OnKey(VirtualKeys.LeftShift, true, 1000, None);
        detector.OnKey(0x41, true, 1020, None);
        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1080, None));
    }

    [Fact]
    public void Drags_that_do_not_start_on_a_shell_surface_are_ignored()
    {
        var detector = StartDrag(shell: false);

        Assert.False(detector.IsDragging);
        detector.OnKey(VirtualKeys.LeftShift, true, 1000, None);
        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1050, None));
    }

    [Fact]
    public void Disabled_detector_never_activates()
    {
        var detector = new DragGestureDetector(4, 4) { Enabled = false };
        detector.OnButtonDown(new ScreenPoint(0, 0), true, false);
        detector.OnMove(new ScreenPoint(50, 0), Shift);

        Assert.False(detector.WantsMoves);
    }

    [Fact]
    public void Activation_happens_once_per_drag()
    {
        var detector = StartDrag();
        detector.OnKey(VirtualKeys.LeftShift, true, 1000, None);
        detector.OnKey(VirtualKeys.LeftShift, false, 1050, None);

        detector.OnKey(VirtualKeys.LeftShift, true, 1200, None);
        Assert.Equal(GestureSignal.None, detector.OnKey(VirtualKeys.LeftShift, false, 1250, None));
    }

    [Fact]
    public void Hold_mode_activates_when_shift_is_down_during_the_drag()
    {
        var detector = new DragGestureDetector(4, 4) { Mode = ActivationMode.ShiftHold };
        detector.OnButtonDown(new ScreenPoint(0, 0), true, false);
        Assert.Equal(GestureSignal.DragStarted, detector.OnMove(new ScreenPoint(10, 0), None));
        Assert.Equal(GestureSignal.Activate, detector.OnMove(new ScreenPoint(12, 0), Shift));
        Assert.Equal(GestureSignal.None, detector.OnMove(new ScreenPoint(14, 0), Shift));
    }

    [Fact]
    public void Hold_mode_activates_on_shift_press_without_further_movement()
    {
        var detector = StartDrag(ActivationMode.ShiftHold);

        Assert.Equal(GestureSignal.Activate, detector.OnKey(VirtualKeys.RightShift, true, 1000, None));
    }

    [Fact]
    public void Hold_mode_ignores_control_shift_which_explorer_uses_for_shortcuts()
    {
        var detector = new DragGestureDetector(4, 4) { Mode = ActivationMode.ShiftHold };
        detector.OnButtonDown(new ScreenPoint(0, 0), true, false);
        Assert.Equal(GestureSignal.DragStarted, detector.OnMove(new ScreenPoint(10, 0), new ModifierState(true, true, false)));
    }

    [Fact]
    public void Escape_during_a_drag_is_reported()
    {
        var detector = StartDrag();

        Assert.Equal(GestureSignal.Escape, detector.OnKey(VirtualKeys.Escape, true, 1000, None));
    }

    [Fact]
    public void Releasing_the_button_ends_a_tracked_drag_and_resets_state()
    {
        var detector = StartDrag();

        Assert.Equal(GestureSignal.DragEnded, detector.OnButtonUp());
        Assert.False(detector.IsDragging);
        Assert.Equal(GestureSignal.None, detector.OnButtonUp());
    }

    [Fact]
    public void Plain_click_does_not_report_drag_end()
    {
        var detector = new DragGestureDetector(4, 4);
        detector.OnButtonDown(new ScreenPoint(0, 0), true, false);

        Assert.Equal(GestureSignal.None, detector.OnButtonUp());
    }

    [Theory]
    [InlineData("CabinetWClass", true)]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Notepad", false)]
    [InlineData("", false)]
    public void Shell_surface_classes(string className, bool expected)
    {
        Assert.Equal(expected, ShellWindowClasses.IsFileSurface(className));
    }
}
