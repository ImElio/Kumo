using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kumo.Windows.Shell;

/// <summary>
/// A hidden top-level window that receives tray callbacks, hotkeys and shell broadcasts.
/// </summary>
/// <remarks>
/// A message-only window (HWND_MESSAGE) would be lighter, but it does not receive broadcasts such as
/// TaskbarCreated, which is needed to restore the tray icon after Explorer restarts.
/// It must be created on a thread that pumps messages; in Kumo that is the WPF dispatcher thread.
/// </remarks>
public sealed unsafe partial class MessageWindow : IDisposable
{
    public delegate bool MessageHandler(uint message, nint wParam, nint lParam, out nint result);

    private const string ClassName = "Kumo.MessageWindow";
    private static readonly Dictionary<nint, MessageWindow> s_windows = [];
    private static ushort s_classAtom;

    private readonly List<MessageHandler> _handlers = [];

    public MessageWindow()
    {
        EnsureClass();
        Handle = CreateWindowExW(0, ClassName, "Kumo", 0, 0, 0, 0, 0, 0, 0, GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the Kumo message window.");
        s_windows[Handle] = this;
    }

    public nint Handle { get; private set; }

    public void AddHandler(MessageHandler handler) => _handlers.Add(handler);

    public void RemoveHandler(MessageHandler handler) => _handlers.Remove(handler);

    public void Dispose()
    {
        if (Handle == 0)
            return;
        s_windows.Remove(Handle);
        DestroyWindow(Handle);
        Handle = 0;
    }

    private static void EnsureClass()
    {
        if (s_classAtom != 0)
            return;
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = GetModuleHandleW(null),
                lpszClassName = name,
            };
            s_classAtom = RegisterClassExW(in wc);
        }
        if (s_classAtom == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not register the Kumo window class.");
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (s_windows.TryGetValue(hwnd, out var window))
        {
            foreach (var handler in window._handlers.ToArray())
            {
                try
                {
                    if (handler(message, wParam, lParam, out nint result))
                        return result;
                }
                catch (Exception ex)
                {
                    // An exception escaping a window procedure would terminate the process.
                    window.UnhandledException?.Invoke(ex);
                }
            }
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public event Action<Exception>? UnhandledException;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(in WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);
}
