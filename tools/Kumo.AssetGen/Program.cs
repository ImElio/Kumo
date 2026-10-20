using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kumo.AssetGen;

/// <summary>
/// Turns the mascot artwork in assets/mascot/kumo-&lt;state&gt;.svg (raster images wrapped in SVG) into the images the
/// app embeds, the application icon and a preview sheet of all states.
/// </summary>
internal static partial class Program
{
    private static readonly string[] States = ["idle", "dragging", "hovering", "converting", "compressing", "processing", "success", "error", "cancelled"];
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private const int AppImageSize = 256;

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string mascot = Path.Combine(root, "assets", "mascot");
        string icons = Path.Combine(root, "assets", "icons");
        string appImages = Directory.CreateDirectory(Path.Combine(root, "src", "Kumo.App", "Mascot", "Images")).FullName;

        var trimmed = new Dictionary<string, BitmapSource>();
        foreach (string state in States)
        {
            var source = LoadEmbeddedPng(Path.Combine(mascot, $"kumo-{state}.svg"));
            trimmed[state] = TrimToSquare(source);
            File.WriteAllBytes(Path.Combine(appImages, $"{state}.png"), Png(Scale(trimmed[state], AppImageSize)));
        }

        File.WriteAllBytes(Path.Combine(icons, "kumo.ico"), Icon(trimmed["idle"]));
        File.WriteAllBytes(Path.Combine(icons, "kumo-256.png"), Png(Scale(trimmed["idle"], 256)));
        File.WriteAllBytes(Path.Combine(mascot, "states.png"), StateSheet(trimmed));
        File.WriteAllBytes(Path.Combine(root, "packaging", "installer", "sidebar.bmp"), InstallerSidebar(trimmed["idle"]));
        Console.WriteLine($"Assets written under {root}");
        return 0;
    }

    [GeneratedRegex("href=\"data:image/png;base64,([^\"]+)\"")]
    private static partial Regex EmbeddedPng();

    private static BitmapSource LoadEmbeddedPng(string svgPath)
    {
        var match = EmbeddedPng().Match(File.ReadAllText(svgPath));
        if (!match.Success)
            throw new InvalidDataException($"{svgPath} does not contain an embedded PNG image.");
        using var stream = new MemoryStream(Convert.FromBase64String(match.Groups[1].Value));
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
    }

    /// <summary>Crops the transparent margin around the drawing and centres it on a square canvas.</summary>
    private static BitmapSource TrimToSquare(BitmapSource image)
    {
        int width = image.PixelWidth, height = image.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        image.CopyPixels(pixels, stride, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * stride + x * 4 + 3] < 16)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0)
            return image;

        int side = Math.Max(right - left + 1, bottom - top + 1);
        int padding = side / 40;
        side += padding * 2;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var content = new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
            double x = (side - content.PixelWidth) / 2.0, y = (side - content.PixelHeight) / 2.0;
            context.DrawImage(content, new Rect(x, y, content.PixelWidth, content.PixelHeight));
        }
        var result = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        return result;
    }

    private static BitmapSource Scale(BitmapSource image, int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
            context.DrawImage(image, new Rect(0, 0, size, size));
        var result = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        return result;
    }

    private static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The welcome and finish page image of the NSIS installer: a pink dusk landscape with Kumo floating above it.
    /// NSIS needs a 24-bit BMP; it is drawn at twice the 164 × 314 page size so it stays sharp on scaled displays.
    /// </summary>
    private static byte[] InstallerSidebar(BitmapSource kumo)
    {
        const int width = 328, height = 628;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Gradient(height, (0, "#FFE9E4F7"), (0.45, "#FFFBD3E0"), (0.75, "#FFF9B9C9")), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xCC, 0xFF, 0xF4, 0xE8), Color.FromArgb(0, 0xFF, 0xD6, 0xE2)),
                null, new Point(width * 0.72, height * 0.56), 120, 120);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xEA)), null, new Point(width * 0.72, height * 0.56), 34, 34);

            Cloud(dc, 46, 118, 0.8, 0x99);
            Cloud(dc, 250, 180, 0.55, 0x77);
            Cloud(dc, 120, 330, 0.45, 0x66);

            Hills(dc, width, height, height * 0.60, 26, 2.1, 0.4, "#FFF0A5BC");
            Hills(dc, width, height, height * 0.68, 32, 1.6, 1.9, "#FFE48AA6");
            Hills(dc, width, height, height * 0.77, 28, 2.6, 0.9, "#FFC96F8E");
            Hills(dc, width, height, height * 0.87, 22, 1.3, 2.7, "#FFA9577A");

            double size = 200;
            var logo = new Rect((width - size) / 2, height * 0.20, size, size);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF)),
                null, new Point(logo.X + size / 2, logo.Y + size / 2), size * 0.62, size * 0.62);
            dc.DrawImage(kumo, logo);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static LinearGradientBrush Gradient(double height, params (double Offset, string Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), offset));
        return brush;
    }

    private static void Hills(DrawingContext dc, double width, double height, double baseline, double amplitude, double waves, double phase, string color)
    {
        var figure = new PathFigure { StartPoint = new Point(0, height), IsClosed = true };
        for (int x = 0; x <= width; x += 4)
        {
            double t = x / width * Math.PI * waves + phase;
            figure.Segments.Add(new LineSegment(new Point(x, baseline - amplitude * (Math.Sin(t) * 0.7 + Math.Sin(t * 2.3 + 1) * 0.3)), false));
        }
        figure.Segments.Add(new LineSegment(new Point(width, height), false));
        dc.DrawGeometry(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), null, new PathGeometry([figure]));
    }

    private static void Cloud(DrawingContext dc, double x, double y, double scale, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));
        foreach (var (dx, dy, r) in new[] { (0.0, 0.0, 22.0), (24.0, -10.0, 28.0), (52.0, 0.0, 20.0), (26.0, 8.0, 22.0) })
            dc.DrawEllipse(brush, null, new Point(x + dx * scale, y + dy * scale), r * scale, r * scale * 0.8);
    }

    private static byte[] StateSheet(Dictionary<string, BitmapSource> images)
    {
        const int cell = 160;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEC)), null, new Rect(0, 0, cell * States.Length, cell + 28));
            for (int i = 0; i < States.Length; i++)
            {
                context.DrawImage(images[States[i]], new Rect(i * cell + 10, 6, cell - 20, cell - 20));
                var label = new FormattedText(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(States[i]), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.Black, 1);
                context.DrawText(label, new Point(i * cell + (cell - label.Width) / 2, cell));
            }
        }
        var bitmap = new RenderTargetBitmap(cell * States.Length, cell + 28, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Png(bitmap);
    }

    // ICO with PNG-compressed entries, supported by Windows Vista and later.
    private static byte[] Icon(BitmapSource image)
    {
        var entries = IconSizes.Select(size => (size, data: Png(Scale(image, size)))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);
        int offset = 6 + 16 * entries.Count;
        foreach (var (size, data) in entries)
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
        foreach (var (_, data) in entries)
            writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }
}
