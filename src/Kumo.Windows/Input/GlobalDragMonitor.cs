using System.ComponentModel;
using System.Runtime.InteropServices;
using Kumo.Windows.Interop;
using static Kumo.Windows.Interop.NativeMethods;

namespace Kumo.Windows.Input;

public enum InputSignal
{
    Activate,
    DragEnded,
    Escape,
    PointerDown,
}

public readonly record struct InputEvent(InputSignal Signal, ScreenPoint Position);

/// <summary>
/// Observes global mouse input through a low-level hook on a dedicated thread and reports Kumo gestures.
/// </summary>
/// <remarks>
/// The hook never swallows input: every event is forwarded with CallNextHookEx, so Explorer's own drag-and-drop
/// behaviour is untouched. The thread does nothing but pump hook messages because Windows silently removes a
/// low-level hook whose callback stalls past LowLevelHooksTimeout. A keyboard hook is installed only while a
/// drag from a shell surface is in progress and removed as soon as the button is released, so keystrokes are
/// not observed at any other time. Callbacks run on the hook thread and must return quickly.
/// </remarks>
public sealed unsafe class GlobalDragMonitor : IDisposable
{
    private const uint InstallKeyboardHookMessage = WM_APP + 1;
    private const uint RemoveKeyboardHookMessage = WM_APP + 2;

    private static GlobalDragMonitor? s_current;

    private readonly DragGestureDetector _detector;
    private readonly Action<InputEvent> _sink;
    private readonly Action<Exception>? _faultSink;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _mouseHook;
    private nint _keyboardHook;
    private int _startError;
    private volatile bool _reportPointerDowns;
    private bool _disposed;

    public GlobalDragMonitor(Action<InputEvent> sink, Action<Exception>? faultSink = null)
    {
        _sink = sink;
        _faultSink = faultSink;
        _detector = new DragGestureDetector(GetSystemMetrics(SM_CXDRAG), GetSystemMetrics(SM_CYDRAG));
    }

    public ActivationMode Mode
    {
        get => _detector.Mode;
        set => _detector.Mode = value;
    }

    public bool GesturesEnabled
    {
        get => _detector.Enabled;
        set => _detector.Enabled = value;
    }

    /// <summary>When set, every left or right button press anywhere is reported, used to dismiss a menu on outside clicks.</summary>
    public bool ReportPointerDowns
    {
        get => _reportPointerDowns;
        set => _reportPointerDowns = value;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != null)
            return;
        if (Interlocked.CompareExchange(ref s_current, this, null) != null)
            throw new InvalidOperationException("Only one global drag monitor can run per process.");

        _thread = new Thread(Pump) { IsBackground = true, Name = "Kumo input hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait();
        if (_mouseHook == 0)
        {
            _thread.Join();
            _thread = null;
            Interlocked.CompareExchange(ref s_current, null, this);
            throw new Win32Exception(_startError, "The global mouse hook could not be installed.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_thread != null)
        {
            PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
        }
        Interlocked.CompareExchange(ref s_current, null, this);
        _ready.Dispose();
    }

    private void Pump()
    {
        _threadId = GetCurrentThreadId();
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, &MouseProc, GetModuleHandleW(null), 0);
        if (_mouseHook == 0)
            _startError = Marshal.GetLastPInvokeError();
        _ready.Set();
        if (_mouseHook == 0)
            return;

        while (GetMessageW(out MSG msg, 0, 0, 0) > 0)
        {
            if (msg.hwnd == 0 && msg.message == InstallKeyboardHookMessage)
            {
                InstallKeyboardHook();
                continue;
            }
            if (msg.hwnd == 0 && msg.message == RemoveKeyboardHookMessage)
            {
                RemoveKeyboardHook();
                continue;
            }
            TranslateMessage(in msg);
            DispatchMessageW(in msg);
        }

        RemoveKeyboardHook();
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook == 0 && _detector.IsDragging)
            _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, GetModuleHandleW(null), 0);
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHook == 0)
            return;
        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = 0;
    }

    // Exceptions must never unwind through an UnmanagedCallersOnly frame; that would terminate the process.
    [UnmanagedCallersOnly]
    private static nint MouseProc(int code, nint wParam, nint lParam)
    {
        var monitor = s_current;
        if (code >= 0 && monitor != null)
        {
            try
            {
                monitor.OnMouse((int)wParam, (MSLLHOOKSTRUCT*)lParam);
            }
            catch (Exception ex)
            {
                monitor.ReportFault(ex);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        var monitor = s_current;
        if (code >= 0 && monitor != null)
        {
            try
            {
                monitor.OnKey((int)wParam, (KBDLLHOOKSTRUCT*)lParam);
            }
            catch (Exception ex)
            {
                monitor.ReportFault(ex);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void OnMouse(int message, MSLLHOOKSTRUCT* data)
    {
        var point = new ScreenPoint(data->pt.X, data->pt.Y);
        switch (message)
        {
            case WM_MOUSEMOVE:
                if (!_detector.WantsMoves)
                    return;
                var signal = _detector.OnMove(point, ReadModifiers());
                if (signal is GestureSignal.DragStarted or GestureSignal.Activate)
                    PostThreadMessageW(_threadId, InstallKeyboardHookMessage, 0, 0);
                Emit(signal, point);
                break;

            case WM_LBUTTONDOWN:
                if (_reportPointerDowns)
                    _sink(new InputEvent(InputSignal.PointerDown, point));
                _detector.OnButtonDown(point, IsShellSurface(data->pt), IsKeyDown(VirtualKeys.Shift));
                break;

            case WM_RBUTTONDOWN:
                if (_reportPointerDowns)
                    _sink(new InputEvent(InputSignal.PointerDown, point));
                break;

            case WM_LBUTTONUP:
                var ended = _detector.OnButtonUp();
                if (_keyboardHook != 0)
                    PostThreadMessageW(_threadId, RemoveKeyboardHookMessage, 0, 0);
                Emit(ended, point);
                break;
        }
    }

    private void OnKey(int message, KBDLLHOOKSTRUCT* data)
    {
        bool isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isUp = message is WM_KEYUP or WM_SYSKEYUP;
        if (!isDown && !isUp)
            return;

        var others = new ModifierState(false, IsKeyDown(VirtualKeys.Control), IsKeyDown(VirtualKeys.Menu));
        var signal = _detector.OnKey((int)data->vkCode, isDown, data->time, others);
        Emit(signal, _detector.Position);
    }

    private void Emit(GestureSignal signal, ScreenPoint point)
    {
        switch (signal)
        {
            case GestureSignal.Activate:
                _sink(new InputEvent(InputSignal.Activate, point));
                break;
            case GestureSignal.DragEnded:
                _sink(new InputEvent(InputSignal.DragEnded, point));
                break;
            case GestureSignal.Escape:
                _sink(new InputEvent(InputSignal.Escape, point));
                break;
        }
    }

    private void ReportFault(Exception ex)
    {
        try
        {
            _faultSink?.Invoke(ex);
        }
        catch (Exception)
        {
            // Nothing sensible can be done if fault reporting itself fails inside a hook callback.
        }
    }

    private static ModifierState ReadModifiers() =>
        new(IsKeyDown(VirtualKeys.Shift), IsKeyDown(VirtualKeys.Control), IsKeyDown(VirtualKeys.Menu));

    private static bool IsShellSurface(POINT point)
    {
        nint window = WindowFromPoint(point);
        if (window == 0)
            return false;
        nint root = GetAncestor(window, GA_ROOT);
        char* buffer = stackalloc char[32];
        int length = GetClassName(root, buffer, 32);
        var name = new ReadOnlySpan<char>(buffer, Math.Max(0, length));
        return ShellWindowClasses.IsFileSurface(name);
    }
}

public static class ShellWindowClasses
{
    /// <summary>Explorer windows and the desktop (Progman, or WorkerW once a wallpaper slideshow has run).</summary>
    public static bool IsFileSurface(ReadOnlySpan<char> className) =>
        className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
}
