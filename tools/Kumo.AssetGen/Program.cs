using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kumo.App.Mascot;

namespace Kumo.AssetGen;

/// <summary>Regenerates the mascot SVGs, the state sheet and the application icon from <see cref="MascotArt"/>.</summary>
internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        string assets = Path.GetFullPath(args.Length > 0 ? args[0] : "assets");
        string mascot = Directory.CreateDirectory(Path.Combine(assets, "mascot")).FullName;
        string icons = Directory.CreateDirectory(Path.Combine(assets, "icons")).FullName;

        foreach (var state in Enum.GetValues<MascotState>())
            File.WriteAllText(Path.Combine(mascot, $"kumo-{state.ToString().ToLowerInvariant()}.svg"), Svg(state), new UTF8Encoding(false));

        File.WriteAllBytes(Path.Combine(icons, "kumo.ico"), Icon(MascotState.Idle));
        File.WriteAllBytes(Path.Combine(icons, "kumo-256.png"), Png(MascotState.Idle, 256));
        File.WriteAllBytes(Path.Combine(mascot, "states.png"), StateSheet());
        Console.WriteLine($"Assets written to {assets}");
        return 0;
    }

    private static string Svg(MascotState state)
    {
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" width=\"100\" height=\"100\">\n");
        svg.Append(CultureInfo.InvariantCulture, $"  <title>Kumo — {state}</title>\n");
        foreach (var part in MascotArt.Parts(state))
        {
            svg.Append(CultureInfo.InvariantCulture, $"  <path d=\"{part.Data}\" fill=\"{Css(part.Fill)}\"");
            if (part.Stroke is { } stroke)
                svg.Append(CultureInfo.InvariantCulture, $" stroke=\"{Css(stroke)}\" stroke-width=\"{part.StrokeWidth}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
            svg.Append("/>\n");
        }
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static string Css(Color? color)
    {
        if (color is not { } c)
            return "none";
        return c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : string.Create(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})");
    }

    private static byte[] Png(MascotState state, int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            double scale = size / MascotArt.DesignSize;
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawDrawing(MascotArt.Drawing(state));
        }
        return Encode(visual, size, size);
    }

    private static byte[] StateSheet()
    {
        var states = Enum.GetValues<MascotState>();
        const int cell = 160;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEC)), null, new Rect(0, 0, cell * states.Length, cell + 28));
            for (int i = 0; i < states.Length; i++)
            {
                context.PushTransform(new TranslateTransform(i * cell + 10, 6));
                context.PushTransform(new ScaleTransform(1.4, 1.4));
                context.DrawDrawing(MascotArt.Drawing(states[i]));
                context.Pop();
                context.Pop();
                var label = new FormattedText(states[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 14, Brushes.Black, 1);
                context.DrawText(label, new Point(i * cell + (cell - label.Width) / 2, cell));
            }
        }
        return Encode(visual, cell * states.Length, cell + 28);
    }

    private static byte[] Encode(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // ICO with PNG-compressed entries, supported by Windows Vista and later.
    private static byte[] Icon(MascotState state)
    {
        var images = IconSizes.Select(size => (size, data: Png(state, size))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images)
            writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }
}
