using System.Runtime.InteropServices;

namespace Kumo.Windows.Shell;

public sealed record TrayMenuItem(int Id, string Text, bool Checked = false, bool Enabled = true, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(0, string.Empty);

    public bool IsSeparator => Id == 0;
}

public enum TrayNotificationKind
{
    Info,
    Warning,
    Error,
}

/// <summary>Notification area icon built directly on Shell_NotifyIcon, without WinForms.</summary>
public sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x10;
    private const uint IconId = 1;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;
    private const uint NIF_SHOWTIP = 0x80;
    private const uint NIIF_INFO = 0x01;
    private const uint NIIF_WARNING = 0x02;
    private const uint NIIF_ERROR = 0x03;
    private const uint NIIF_RESPECT_QUIET_TIME = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint WM_NULL = 0x0000;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint NIN_BALLOONUSERCLICK = 0x0405;

    private const uint MF_STRING = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTALIGN = 0x0008;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int SM_CXSMICON = 49;
    private const int SM_MENUDROPALIGNMENT = 40;

    private readonly MessageWindow _window;
    private readonly string _iconPath;
    private readonly uint _taskbarCreated;
    private readonly MessageWindow.MessageHandler _handler;
    private string _tooltip;
    private nint _icon;
    private bool _added;

    public TrayIcon(MessageWindow window, string iconPath, string tooltip)
    {
        _window = window;
        _iconPath = iconPath;
        _tooltip = tooltip;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _handler = OnMessage;
        _window.AddHandler(_handler);
    }

    public event Action? Invoked;

    public event Action? NotificationClicked;

    public event Action<int>? MenuCommand;

    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public bool IsVisible => _added;

    public void Show()
    {
        LoadIcon();
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_added)
            return;
        data.uVersionOrTimeout = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (!_added)
            return;
        var data = CreateData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void ShowNotification(string title, string text, TrayNotificationKind kind)
    {
        if (!_added)
            return;
        var data = CreateData(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = NIIF_RESPECT_QUIET_TIME | kind switch
        {
            TrayNotificationKind.Warning => NIIF_WARNING,
            TrayNotificationKind.Error => NIIF_ERROR,
            _ => NIIF_INFO,
        };
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        _window.RemoveHandler(_handler);
        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            _added = false;
            Show();
            return true;
        }
        if (message != CallbackMessage)
            return false;

        switch ((uint)(lParam & 0xFFFF))
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                Invoked?.Invoke();
                break;
            case WM_CONTEXTMENU:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
            case NIN_BALLOONUSERCLICK:
                NotificationClicked?.Invoke();
                break;
        }
        return true;
    }

    private void ShowMenu(int x, int y)
    {
        var items = MenuProvider?.Invoke();
        if (items == null || items.Count == 0)
            return;

        nint menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MF_SEPARATOR, 0, null);
                    continue;
                }
                uint flags = MF_STRING | (item.Checked ? MF_CHECKED : 0) | (item.Enabled ? 0 : MF_GRAYED);
                AppendMenuW(menu, flags, (nuint)item.Id, item.Text);
                if (item.IsDefault)
                    SetMenuDefaultItem(menu, (uint)item.Id, 0);
            }

            // Without taking the foreground first, the menu would not close when the user clicks elsewhere.
            SetForegroundWindow(_window.Handle);
            uint align = GetSystemMetrics(SM_MENUDROPALIGNMENT) != 0 ? TPM_RIGHTALIGN : 0;
            int command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_BOTTOMALIGN | align, x, y, _window.Handle, 0);
            PostMessageW(_window.Handle, WM_NULL, 0, 0);
            if (command != 0)
                MenuCommand?.Invoke(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void LoadIcon()
    {
        if (_icon != 0)
            DestroyIcon(_icon);
        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        _icon = LoadImageW(0, _iconPath, 1, size, size, 0x10);
    }

    private NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(_tooltip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersionOrTimeout;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(nint hMenu, uint uItem, uint fByPos);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
