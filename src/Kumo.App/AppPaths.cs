using System.IO;

namespace Kumo.App;

internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kumo");

    public static string Settings { get; } = Path.Combine(Root, "settings.json");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static string Executable { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Kumo.exe");

    public static string TrayIcon { get; } = Path.Combine(AppContext.BaseDirectory, "kumo.ico");
}
