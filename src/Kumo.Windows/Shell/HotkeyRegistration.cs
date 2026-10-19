using System.Runtime.InteropServices;

namespace Kumo.Windows.Shell;

/// <summary>A system-wide shortcut registered with RegisterHotKey on the Kumo message window.</summary>
public sealed partial class HotkeyRegistration : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int Id = 0x4B55;

    private readonly MessageWindow _window;
    private readonly MessageWindow.MessageHandler _handler;
    private bool _registered;

    public HotkeyRegistration(MessageWindow window)
    {
        _window = window;
        _handler = OnMessage;
        _window.AddHandler(_handler);
    }

    public event Action? Pressed;

    /// <param name="modifiers">MOD_ALT (1), MOD_CONTROL (2), MOD_SHIFT (4) and MOD_WIN (8) flags.</param>
    /// <returns>False when another application already owns the combination.</returns>
    public bool Register(uint modifiers, uint virtualKey)
    {
        Unregister();
        _registered = RegisterHotKey(_window.Handle, Id, modifiers | MOD_NOREPEAT, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        UnregisterHotKey(_window.Handle, Id);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHandler(_handler);
    }

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (message != WM_HOTKEY || wParam != Id)
            return false;
        Pressed?.Invoke();
        return true;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
