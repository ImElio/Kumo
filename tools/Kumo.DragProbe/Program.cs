using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Kumo.DragProbe;

/// <summary>
/// Drives real drags out of a File Explorer window with synthesized input and checks Kumo's log for the outcome.
/// It takes over the mouse for a few seconds and aborts as soon as the pointer is moved by hand.
/// </summary>
internal static partial class Program
{
    private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kumo", "logs");

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Kumo.DragProbe <path-to-Kumo.exe> <sample-file> [scenario...]");
            return 2;
        }

        string kumoPath = Path.GetFullPath(args[0]);
        string sample = Path.GetFullPath(args[1]);
        string[] scenarios = args.Length > 2 ? args[2..] : ["drop", "escape", "elsewhere", "hold"];
        string folder = Path.GetDirectoryName(sample)!;

        using var kumo = StartKumo(kumoPath);
        AutomationElement? explorer = null;
        int failures = 0;
        try
        {
            explorer = OpenExplorer(folder);
            foreach (string scenario in scenarios)
            {
                var item = FindItem(explorer, Path.GetFileNameWithoutExtension(sample));
                var (passed, detail) = scenario switch
                {
                    "drop" => DropOnSegment(item, kumo.Id, sample),
                    "escape" => CancelWithEscape(item, sample),
                    "elsewhere" => ReleaseElsewhere(item, explorer, sample),
                    "hold" => HoldShiftKeepsExplorerSemantics(item, sample),
                    _ => (false, "unknown scenario"),
                };
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {scenario}: {detail}");
                if (!passed)
                    failures++;
                Thread.Sleep(600);
            }
        }
        catch (ProbeAbortedException ex)
        {
            Console.WriteLine($"ABORTED: {ex.Message}");
            failures++;
        }
        finally
        {
            Input.ReleaseAll();
            if (explorer != null && explorer.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
                ((WindowPattern)pattern).Close();
            if (!kumo.HasExited)
                kumo.Kill();
        }
        return failures == 0 ? 0 : 1;
    }

    private static Process StartKumo(string path)
    {
        foreach (var existing in Process.GetProcessesByName("Kumo"))
        {
            existing.Kill();
            existing.WaitForExit(2000);
        }
        var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false })!;
        Thread.Sleep(1500);
        return process;
    }

    private static AutomationElement OpenExplorer(string folder)
    {
        string name = Path.GetFileName(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Thread.Sleep(250);
            var window = AutomationElement.RootElement
                .FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "CabinetWClass"))
                .Cast<AutomationElement>()
                .FirstOrDefault(w => w.Current.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (window != null)
            {
                if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
                    ((WindowPattern)pattern).SetWindowVisualState(WindowVisualState.Normal);
                window.SetFocus();
                Thread.Sleep(800);
                return window;
            }
        }
        throw new ProbeAbortedException("Explorer window did not appear.");
    }

    private static System.Windows.Point FindItem(AutomationElement explorer, string stem)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var items = explorer.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            foreach (AutomationElement item in items)
            {
                if (item.Current.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                {
                    var r = item.Current.BoundingRectangle;
                    return new System.Windows.Point(r.Left + Math.Min(r.Width / 2, 40), r.Top + r.Height / 2);
                }
            }
            Thread.Sleep(250);
        }
        throw new ProbeAbortedException("Sample file not found in the Explorer window.");
    }

    private static (bool, string) DropOnSegment(System.Windows.Point item, int kumoPid, string sample)
    {
        long mark = LogMark();
        var drag = Input.BeginDrag(item);
        Input.TapShift();
        Thread.Sleep(400);
        var overlay = FindOverlay(kumoPid);
        if (overlay == null)
        {
            Input.LeftUp();
            return (false, "overlay window not found after Shift tap");
        }
        var r = overlay.Current.BoundingRectangle;
        double scale = r.Width / 340;
        var target = new System.Windows.Point(r.Left + r.Width / 2, r.Top + r.Height / 2 - 111 * scale);
        Input.MoveSmoothly(drag, target, 12);
        Thread.Sleep(300);
        Input.LeftUp();
        Thread.Sleep(900);
        var lines = LogSince(mark);
        bool activated = lines.Any(l => l.Contains("\"Overlay activated\""));
        bool dropped = lines.Any(l => l.Contains("\"Drop accepted\"") && l.Contains("\"action\":\"Convert\""));
        bool intact = File.Exists(sample);
        return (activated && dropped && intact, $"activated={activated} dropAccepted(Convert)={dropped} sourceIntact={intact}");
    }

    private static (bool, string) CancelWithEscape(System.Windows.Point item, string sample)
    {
        long mark = LogMark();
        Input.BeginDrag(item);
        Input.TapShift();
        Thread.Sleep(400);
        Input.Key(0x1B, down: true);
        Input.Key(0x1B, down: false);
        Thread.Sleep(300);
        Input.LeftUp();
        Thread.Sleep(700);
        var lines = LogSince(mark);
        bool activated = lines.Any(l => l.Contains("\"Overlay activated\""));
        bool dismissed = lines.Any(l => l.Contains("\"reason\":\"escape\""));
        bool noDrop = !lines.Any(l => l.Contains("\"Drop accepted\""));
        bool intact = File.Exists(sample);
        return (activated && dismissed && noDrop && intact, $"activated={activated} escapeDismissed={dismissed} noDrop={noDrop} sourceIntact={intact}");
    }

    // Releases over empty space in the bottom-right of the same Explorer view, away from the overlay and from
    // any folder the file could be moved into.
    private static (bool, string) ReleaseElsewhere(System.Windows.Point item, AutomationElement explorer, string sample)
    {
        long mark = LogMark();
        var drag = Input.BeginDrag(item);
        Input.TapShift();
        Thread.Sleep(400);
        var bounds = explorer.Current.BoundingRectangle;
        Input.MoveSmoothly(drag, new System.Windows.Point(bounds.Right - 60, bounds.Bottom - 70), 16);
        Thread.Sleep(250);
        Input.LeftUp();
        Thread.Sleep(900);
        var lines = LogSince(mark);
        bool activated = lines.Any(l => l.Contains("\"Overlay activated\""));
        bool dismissed = lines.Any(l => l.Contains("\"reason\":\"released-elsewhere\""));
        bool intact = File.Exists(sample);
        return (activated && dismissed && intact, $"activated={activated} dismissedAfterRelease={dismissed} sourceIntact={intact}");
    }

    private static (bool, string) HoldShiftKeepsExplorerSemantics(System.Windows.Point item, string sample)
    {
        long mark = LogMark();
        var drag = Input.BeginDrag(item);
        Input.Key(0xA0, down: true);
        Thread.Sleep(700);
        Input.MoveSmoothly(drag, item, 10);
        Input.Key(0xA0, down: false);
        Thread.Sleep(150);
        Input.LeftUp();
        Thread.Sleep(700);
        var lines = LogSince(mark);
        bool activated = lines.Any(l => l.Contains("\"Overlay activated\""));
        bool intact = File.Exists(sample);
        return (!activated && intact, $"overlayStayedClosed={!activated} sourceIntact={intact}");
    }

    private static AutomationElement? FindOverlay(int pid)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var found = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
                new PropertyCondition(AutomationElement.NameProperty, "Kumo")));
            if (found != null && !found.Current.BoundingRectangle.IsEmpty)
                return found;
            Thread.Sleep(100);
        }
        return null;
    }

    private static string CurrentLog => Path.Combine(LogDirectory, $"kumo-{DateTime.Now:yyyyMMdd}.log");

    private static long LogMark() => File.Exists(CurrentLog) ? new FileInfo(CurrentLog).Length : 0;

    private static List<string> LogSince(long mark)
    {
        if (!File.Exists(CurrentLog))
            return [];
        using var stream = new FileStream(CurrentLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(mark, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}

internal sealed class ProbeAbortedException(string message) : Exception(message);
