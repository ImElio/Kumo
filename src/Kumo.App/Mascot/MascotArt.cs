using System.Windows;
using System.Windows.Media;

namespace Kumo.App.Mascot;

public enum MascotState
{
    Idle,
    Dragging,
    Hovering,
    Converting,
    Compressing,
    Processing,
    Success,
    Error,
    Cancelled,
}

/// <summary>
/// Kumo's artwork, defined once in a 100×100 design space. Path data uses the subset of syntax shared by WPF
/// and SVG so the same definitions also produce the SVG and ICO assets (see tools/Kumo.AssetGen).
/// </summary>
public static class MascotArt
{
    public const double DesignSize = 100;

    public static readonly Color Body = Color.FromRgb(0xFF, 0xF6, 0xE6);
    public static readonly Color Outline = Color.FromRgb(0x4A, 0x3B, 0x33);
    public static readonly Color Ink = Color.FromRgb(0x2E, 0x24, 0x20);
    public static readonly Color Blush = Color.FromArgb(0xC0, 0xF6, 0xA9, 0xA0);
    public static readonly Color Scarf = Color.FromRgb(0xF2, 0x8A, 0x30);
    public static readonly Color ScarfShade = Color.FromRgb(0xD9, 0x6F, 0x1E);
    public static readonly Color Highlight = Colors.White;

    public sealed record Part(string Data, Color? Fill, Color? Stroke = null, double StrokeWidth = 0);

    private const string BodyPath = "M 24,76 C 11,76 5,66 9,57 C 4,46 14,37 25,40 C 26,25 41,17 53,21 C 63,13 81,19 81,33 C 93,34 99,46 95,56 C 100,67 91,77 79,76 Q 51,80 24,76 Z";
    private const string ScarfTailPath = "M 64,72 L 71,91 Q 66,94 60,91 L 56,74 Z";
    private const string ScarfBandPath = "M 15,65 Q 51,77 89,63 L 91,70 Q 52,86 17,73 Z";
    private const string KnotPath = "M 57,73 A 5,5 0 1 1 67,73 A 5,5 0 1 1 57,73 Z";

    private static readonly Dictionary<MascotState, Drawing> s_cache = [];

    public static IReadOnlyList<Part> Parts(MascotState state)
    {
        var parts = new List<Part>
        {
            new(ScarfTailPath, ScarfShade, Outline, 2.2),
            new(BodyPath, Body, Outline, 2.6),
            new(Ellipse(34, 57, 4.6, 2.6), Blush),
            new(Ellipse(68, 57, 4.6, 2.6), Blush),
        };
        parts.AddRange(Face(state));
        parts.Add(new(ScarfBandPath, Scarf, Outline, 2.2));
        parts.Add(new(KnotPath, Scarf, Outline, 2.2));
        return parts;
    }

    /// <summary>A frozen drawing in design coordinates, safe to share across threads and windows.</summary>
    public static Drawing Drawing(MascotState state)
    {
        lock (s_cache)
        {
            if (s_cache.TryGetValue(state, out var cached))
                return cached;

            var group = new DrawingGroup();
            foreach (var part in Parts(state))
            {
                var geometry = Geometry.Parse(part.Data);
                Brush? fill = part.Fill is { } f ? new SolidColorBrush(f) : null;
                Pen? pen = part.Stroke is { } s
                    ? new Pen(new SolidColorBrush(s), part.StrokeWidth) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }
                    : null;
                group.Children.Add(new GeometryDrawing(fill, pen, geometry));
            }
            group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, DesignSize, DesignSize));
            group.Freeze();
            s_cache[state] = group;
            return group;
        }
    }

    private static IEnumerable<Part> Face(MascotState state) => state switch
    {
        MascotState.Dragging =>
        [
            EyeOval(41, 49, 3.8, 5.2), EyeOval(61, 49, 3.8, 5.2),
            Glint(42.4, 47), Glint(62.4, 47),
            Line("M 48,58 Q 51,60.2 54,58"),
        ],
        MascotState.Hovering =>
        [
            EyeOval(42, 48, 3.6, 4.8), EyeOval(62, 48, 3.6, 4.8),
            Glint(43.4, 46.2), Glint(63.4, 46.2),
            new(Ellipse(52, 58, 2.2, 2.6), Ink),
        ],
        MascotState.Converting =>
        [
            EyeOval(43, 50, 3.2, 4.2), EyeOval(63, 50, 3.2, 4.2),
            Line("M 37,44.5 L 44,43.5"), Line("M 58,43.5 L 65,44.5"),
            Line("M 48,58 Q 51,59.4 54,58"),
        ],
        MascotState.Compressing =>
        [
            Line("M 37,46.5 L 43.5,50 L 37,53.5"), Line("M 65,46.5 L 58.5,50 L 65,53.5"),
            Line("M 47,58.5 Q 49,56.6 51,58.5 Q 53,60.4 55,58.5"),
        ],
        MascotState.Processing =>
        [
            Line("M 37.5,50.5 L 44.5,50.5", 3), Line("M 57.5,50.5 L 64.5,50.5", 3),
            Line("M 37,45 L 44,46.2"), Line("M 65,45 L 58,46.2"),
            Line("M 48,58 L 54,58"),
        ],
        MascotState.Success =>
        [
            Line("M 37,51 Q 41,46 45,51", 2.8), Line("M 57,51 Q 61,46 65,51", 2.8),
            new("M 46,56 Q 51,64.5 56,56 Z", Ink, Ink, 1.6),
        ],
        MascotState.Error =>
        [
            EyeOval(41, 51, 3.2, 4), EyeOval(61, 51, 3.2, 4),
            Glint(42.2, 49.6), Glint(62.2, 49.6),
            Line("M 36,46 L 44,43.5"), Line("M 66,46 L 58,43.5"),
            Line("M 47,59.8 Q 51,56.2 55,59.8"),
        ],
        MascotState.Cancelled =>
        [
            new(Ellipse(41, 50, 2.6, 2.6), Ink), new(Ellipse(61, 50, 2.6, 2.6), Ink),
            Line("M 48,58 L 54,58"),
        ],
        _ =>
        [
            EyeOval(41, 50, 3.4, 4.4), EyeOval(61, 50, 3.4, 4.4),
            Glint(42.2, 48.4), Glint(62.2, 48.4),
            Line("M 47.5,57 Q 51,60.5 54.5,57"),
        ],
    };

    private static Part EyeOval(double cx, double cy, double rx, double ry) => new(Ellipse(cx, cy, rx, ry), Ink);

    private static Part Glint(double cx, double cy) => new(Ellipse(cx, cy, 1.2, 1.2), Highlight);

    private static Part Line(string data, double width = 2.4) => new(data, null, Ink, width);

    private static string Ellipse(double cx, double cy, double rx, double ry) =>
        FormattableString.Invariant($"M {cx - rx},{cy} A {rx},{ry} 0 1 1 {cx + rx},{cy} A {rx},{ry} 0 1 1 {cx - rx},{cy} Z");
}
