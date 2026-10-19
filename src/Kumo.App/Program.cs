using Kumo.Windows.Shell;

namespace Kumo.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var instance = new SingleInstance("Kumo");
        if (!instance.IsPrimary)
        {
            instance.TrySend(args.Length > 0 ? args : [CommandLine.Show], TimeSpan.FromSeconds(2));
            return 0;
        }

        var app = new KumoApplication(instance, args);
        return app.Run();
    }
}

internal static class CommandLine
{
    public const string Background = "--background";
    public const string Show = "--show";
    public const string Settings = "--settings";
    public const string About = "--about";

    public static string[] Files(IEnumerable<string> arguments) =>
        arguments.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && System.IO.File.Exists(a)).ToArray();
}
