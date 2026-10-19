using System.Windows.Media;
using Kumo.Core.Operations;

namespace Kumo.App.Overlay;

/// <summary>Stroke icons on a 16×16 grid, drawn by Kumo so they do not depend on an icon font being installed.</summary>
internal static class RadialIcons
{
    public const double GridSize = 16;

    private static readonly Dictionary<OperationCategory, Geometry> s_icons = new()
    {
        [OperationCategory.Convert] = Freeze("M 2.5,5.5 H 12.5 M 10,3 L 12.5,5.5 L 10,8 M 13.5,10.5 H 3.5 M 6,8 L 3.5,10.5 L 6,13"),
        [OperationCategory.Crop] = Freeze("M 4.5,1.5 V 11.5 H 14.5 M 1.5,4.5 H 11.5 V 14.5"),
        [OperationCategory.Compress] = Freeze("M 8,1.5 V 6 M 5.5,3.5 L 8,6 L 10.5,3.5 M 8,14.5 V 10 M 5.5,12.5 L 8,10 L 10.5,12.5 M 2.5,8 H 13.5"),
        [OperationCategory.Rotate] = Freeze("M 12.8,9.5 A 5,5 0 1 1 11.5,4.2 M 12,1.5 V 4.8 H 8.7"),
        [OperationCategory.Merge] = Freeze("M 3.5,2 V 5.5 Q 3.5,8.5 8,9.5 V 14 M 12.5,2 V 5.5 Q 12.5,8.5 8,9.5 M 5.5,12 L 8,14.5 L 10.5,12"),
        [OperationCategory.Split] = Freeze("M 8,1.5 V 6 Q 8,8 3.5,10 V 14.5 M 8,6 Q 8,8 12.5,10 V 14.5"),
        [OperationCategory.Extract] = Freeze("M 9.5,1.5 H 3.5 V 14.5 H 9 M 9.5,1.5 L 12.5,4.5 V 7.5 M 9.5,1.5 V 4.5 H 12.5 M 8.5,11 H 15 M 12.5,8.5 L 15,11 L 12.5,13.5"),
        [OperationCategory.Protect] = Freeze("M 3.5,7.5 H 12.5 V 14.5 H 3.5 Z M 5.5,7.5 V 5 A 2.5,2.5 0 0 1 10.5,5 V 7.5 M 8,10 V 12"),
    };

    public static Geometry Back { get; } = Freeze("M 10,3 L 5,8 L 10,13");

    public static Geometry For(OperationCategory category) => s_icons[category];

    private static Geometry Freeze(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
