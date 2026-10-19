namespace Kumo.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new KumoApplication();
        return app.Run();
    }
}
