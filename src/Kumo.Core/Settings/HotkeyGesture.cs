using System.Globalization;
using System.Text;

namespace Kumo.Core.Settings;

/// <summary>Values match the MOD_* flags accepted by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>
    /// Accepts text such as "Ctrl+Alt+K". At least Ctrl, Alt or Win is required so a plain or Shift-only
    /// combination can never swallow ordinary typing.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        int key = 0;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    if (key != 0)
                        return false;
                    key = KeyFromName(part);
                    if (key == 0)
                        return false;
                    break;
            }
        }

        if (key == 0 || (modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) == 0)
            return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
            text.Append("Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            text.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            text.Append("Shift+");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
            text.Append("Win+");
        text.Append(NameFromKey(VirtualKey));
        return text.ToString();
    }

    public static int KeyFromName(string name)
    {
        string upper = name.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            return upper[0];
        if (upper.Length is 2 or 3 && upper[0] == 'F'
            && int.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;
        return upper switch
        {
            "SPACE" => 0x20,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            _ => 0,
        };
    }

    public static string NameFromKey(int virtualKey) => virtualKey switch
    {
        (>= 'A' and <= 'Z') or (>= '0' and <= '9') => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture),
        0x20 => "Space",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        _ => "?",
    };
}
