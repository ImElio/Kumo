using System.Runtime.InteropServices;
using System.Windows;

namespace Kumo.DragProbe;

internal static partial class Input
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static Point s_last;
    private static bool s_leftDown;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static Point BeginDrag(Point item)
    {
        MoveTo(item);
        Thread.Sleep(150);
        Mouse(MOUSEEVENTF_LEFTDOWN);
        s_leftDown = true;
        Thread.Sleep(120);
        var target = new Point(item.X + 60, item.Y + 20);
        MoveSmoothly(item, target, 12);
        Thread.Sleep(250);
        return target;
    }

    public static void MoveSmoothly(Point from, Point to, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            MoveTo(new Point(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps));
            Thread.Sleep(20);
        }
    }

    public static void TapShift()
    {
        Key(0xA0, down: true);
        Thread.Sleep(90);
        Key(0xA0, down: false);
    }

    public static void Key(ushort vk, bool down)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = down ? 0 : KEYEVENTF_KEYUP;
        Send(input);
    }

    public static void LeftUp()
    {
        GuardPointer();
        Mouse(MOUSEEVENTF_LEFTUP);
        s_leftDown = false;
    }

    public static void ReleaseAll()
    {
        if (s_leftDown)
        {
            Mouse(MOUSEEVENTF_LEFTUP);
            s_leftDown = false;
        }
        Key(0xA0, down: false);
    }

    private static void MoveTo(Point point)
    {
        GuardPointer();
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        var input = new INPUT { type = INPUT_MOUSE };
        input.u.mi.dx = (int)Math.Round((point.X - left) * 65535.0 / (width - 1));
        input.u.mi.dy = (int)Math.Round((point.Y - top) * 65535.0 / (height - 1));
        input.u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        Send(input);
        s_last = point;
    }

    private static void Mouse(uint flags)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.u.mi.dwFlags = flags;
        Send(input);
    }

    private static void GuardPointer()
    {
        if (s_last == default)
            return;
        GetCursorPos(out var p);
        if (Math.Abs(p.X - s_last.X) > 3 || Math.Abs(p.Y - s_last.Y) > 3)
            throw new ProbeAbortedException("The pointer was moved by hand; stopping.");
    }

    private static void Send(INPUT input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1)
            throw new ProbeAbortedException($"SendInput failed ({Marshal.GetLastPInvokeError()}).");
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);
}
