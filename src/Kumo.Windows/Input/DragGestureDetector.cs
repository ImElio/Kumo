namespace Kumo.Windows.Input;

public enum ActivationMode
{
    ShiftTap,
    ShiftHold,
}

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ModifierState(bool Shift, bool Control, bool Alt);

public enum GestureSignal
{
    None,
    DragStarted,
    Activate,
    DragEnded,
    Escape,
}

/// <summary>
/// Turns raw global mouse and keyboard events into Kumo activation requests.
/// It is deliberately free of Win32 calls so the activation rules can be tested in isolation.
/// </summary>
/// <remarks>
/// Explorer already assigns meaning to Shift during a drag (force move). In <see cref="ActivationMode.ShiftTap"/>
/// mode a quick press-and-release of Shift opens Kumo, so holding Shift through the drop keeps its native
/// move semantics. <see cref="ActivationMode.ShiftHold"/> opens Kumo as soon as Shift is held during a drag.
/// </remarks>
public sealed class DragGestureDetector
{
    public const long DefaultTapMilliseconds = 450;

    private readonly int _thresholdX;
    private readonly int _thresholdY;
    private readonly long _tapMilliseconds;

    private bool _buttonDown;
    private bool _candidate;
    private bool _dragging;
    private bool _activated;
    private bool _shiftDown;
    private bool _tapPending;
    private bool _tapSpoiled;
    private long _shiftPressedAt;
    private ScreenPoint _origin;

    public DragGestureDetector(int thresholdX, int thresholdY, long tapMilliseconds = DefaultTapMilliseconds)
    {
        _thresholdX = Math.Max(2, thresholdX);
        _thresholdY = Math.Max(2, thresholdY);
        _tapMilliseconds = tapMilliseconds;
    }

    public ActivationMode Mode { get; set; } = ActivationMode.ShiftTap;

    public bool Enabled { get; set; } = true;

    public ScreenPoint Position { get; private set; }

    /// <summary>True while a left-button press that started on a shell surface is being followed.</summary>
    public bool WantsMoves => _buttonDown && _candidate;

    public bool IsDragging => _buttonDown && _candidate && _dragging;

    public GestureSignal OnButtonDown(ScreenPoint point, bool shellSurface, bool shiftDown)
    {
        _buttonDown = true;
        _candidate = shellSurface && Enabled;
        _dragging = false;
        _activated = false;
        _tapPending = false;
        _tapSpoiled = false;
        _shiftDown = shiftDown;
        _origin = point;
        Position = point;
        return GestureSignal.None;
    }

    public GestureSignal OnMove(ScreenPoint point, ModifierState modifiers)
    {
        if (!WantsMoves)
            return GestureSignal.None;

        Position = point;
        if (!_dragging)
        {
            if (Math.Abs((long)point.X - _origin.X) < _thresholdX && Math.Abs((long)point.Y - _origin.Y) < _thresholdY)
                return GestureSignal.None;
            _dragging = true;
            if (Mode == ActivationMode.ShiftHold && TryActivateOnHold(modifiers))
                return GestureSignal.Activate;
            return GestureSignal.DragStarted;
        }

        return Mode == ActivationMode.ShiftHold && TryActivateOnHold(modifiers)
            ? GestureSignal.Activate
            : GestureSignal.None;
    }

    public GestureSignal OnButtonUp()
    {
        if (!_buttonDown)
            return GestureSignal.None;

        bool wasTracked = _candidate && (_dragging || _activated);
        _buttonDown = false;
        _candidate = false;
        _dragging = false;
        _activated = false;
        _tapPending = false;
        return wasTracked ? GestureSignal.DragEnded : GestureSignal.None;
    }

    /// <summary>Keyboard events are only delivered while a drag is being tracked.</summary>
    public GestureSignal OnKey(int virtualKey, bool isDown, long timestampMilliseconds, ModifierState otherModifiers)
    {
        if (!IsDragging)
            return GestureSignal.None;

        if (virtualKey == VirtualKeys.Escape)
            return isDown ? GestureSignal.Escape : GestureSignal.None;

        if (!VirtualKeys.IsShift(virtualKey))
        {
            if (isDown)
                _tapSpoiled = true;
            return GestureSignal.None;
        }

        if (isDown)
        {
            if (_shiftDown)
                return GestureSignal.None;
            _shiftDown = true;

            if (_activated)
                return GestureSignal.None;

            if (Mode == ActivationMode.ShiftHold)
                return TryActivateOnHold(otherModifiers with { Shift = true }) ? GestureSignal.Activate : GestureSignal.None;

            _tapPending = true;
            _tapSpoiled = otherModifiers.Control || otherModifiers.Alt;
            _shiftPressedAt = timestampMilliseconds;
            return GestureSignal.None;
        }

        _shiftDown = false;
        if (!_tapPending)
            return GestureSignal.None;

        _tapPending = false;
        bool quick = timestampMilliseconds - _shiftPressedAt <= _tapMilliseconds;
        if (_activated || _tapSpoiled || !quick || Mode != ActivationMode.ShiftTap)
            return GestureSignal.None;

        _activated = true;
        return GestureSignal.Activate;
    }

    private bool TryActivateOnHold(ModifierState modifiers)
    {
        if (_activated || !modifiers.Shift || modifiers.Control || modifiers.Alt)
            return false;
        _activated = true;
        return true;
    }
}

public static class VirtualKeys
{
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int Escape = 0x1B;
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;
    public const int LeftMenu = 0xA4;
    public const int RightMenu = 0xA5;

    public static bool IsShift(int vk) => vk is Shift or LeftShift or RightShift;
}
