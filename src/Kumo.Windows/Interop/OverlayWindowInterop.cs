using System.Runtime.InteropServices;
using Kumo.Windows.Input;

namespace Kumo.Windows.Interop;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(ScreenPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
}

/// <summary>Window plumbing for the radial overlay, expressed in physical screen pixels.</summary>
public static partial class OverlayWindowInterop
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private static readonly nint HWND_TOPMOST = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public NativeMethods.RECT rcMonitor;
        public NativeMethods.RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// A tool window that never takes activation: Explorer keeps focus while a drag is in progress
    /// and the overlay does not appear in the taskbar or Alt+Tab.
    /// </summary>
    public static void ApplyOverlayStyles(nint hwnd)
    {
        long style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)style);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>Keyboard interaction requires activation; it is enabled only once Kumo owns the interaction.</summary>
    public static void SetActivatable(nint hwnd, bool activatable)
    {
        long style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        style = activatable ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)style);
    }

    public static bool TryBringToFront(nint hwnd) => SetForegroundWindow(hwnd);

    /// <summary>
    /// Moves the window onto the monitor that contains <paramref name="point"/> without resizing it,
    /// so a per-monitor DPI change is processed by WPF before the final position is computed.
    /// </summary>
    public static void MoveOnto(nint hwnd, ScreenPoint point) =>
        SetWindowPos(hwnd, HWND_TOPMOST, point.X, point.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Centres the window on the point, kept inside the work area of that monitor.</summary>
    public static ScreenRect CenterOn(nint hwnd, ScreenPoint point)
    {
        GetWindowRect(hwnd, out var current);
        int width = current.Right - current.Left;
        int height = current.Bottom - current.Top;
        var work = GetWorkArea(point);
        int x = Clamp(point.X - width / 2, work.Left, work.Right - width);
        int y = Clamp(point.Y - height / 2, work.Top, work.Bottom - height);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        return new ScreenRect(x, y, x + width, y + height);
    }

    public static ScreenRect GetBounds(nint hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return new ScreenRect(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static ScreenRect GetWorkArea(ScreenPoint point)
    {
        nint monitor = MonitorFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
            return new ScreenRect(int.MinValue / 2, int.MinValue / 2, int.MaxValue / 2, int.MaxValue / 2);
        return new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
    }

    /// <summary>Effective DPI scale (1.0 = 96 DPI) of the monitor containing the point.</summary>
    public static double ScaleAt(ScreenPoint point)
    {
        nint monitor = MonitorFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
        return monitor != 0 && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
    }

    public static ScreenPoint GetCursorPosition()
    {
        NativeMethods.GetCursorPos(out var p);
        return new ScreenPoint(p.X, p.Y);
    }

    internal static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out NativeMethods.RECT lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativeMethods.POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
