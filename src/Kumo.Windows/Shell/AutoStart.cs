using Microsoft.Win32;

namespace Kumo.Windows.Shell;

/// <summary>Per-user "start with Windows" entry under HKCU\...\Run. No elevation is required.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kumo";

    public static string Command(string executablePath) => $"\"{executablePath}\" --background";

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value
            && string.Equals(value, Command(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(string executablePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, Command(executablePath), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
