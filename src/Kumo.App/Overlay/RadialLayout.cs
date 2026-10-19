using System.Windows;
using Kumo.Core.Radial;

namespace Kumo.App.Overlay;

/// <summary>Dimensions of the radial menu in device-independent pixels, derived from the ring diameter.</summary>
internal sealed record RadialLayout(double Diameter)
{
    public const double GapDegrees = 2.2;

    public double Margin => 14;

    public double Width => Diameter + Margin * 2;

    /// <summary>Space reserved under the ring for the status bar, mirrored above so the ring stays centred on the window.</summary>
    public double StatusHeight => 56;

    public double Height => Diameter + Margin * 2 + StatusHeight * 2;

    public Point Center => new(Width / 2, Height / 2);

    public double OuterRadius => Diameter / 2;

    public double HubRadius => Diameter * 0.17;

    public double InnerRadius => HubRadius + 5;

    public double ContentRadius => (InnerRadius + OuterRadius) / 2 + 2;

    public double Scale => Diameter / 340;

    public RadialHit HitTest(Point point, int count) =>
        RadialGeometry.HitTest(point.X - Center.X, point.Y - Center.Y, count, HubRadius, OuterRadius);

    public Point At(double radius, double angle)
    {
        var (x, y) = RadialGeometry.PointAt(radius, angle);
        return new Point(Center.X + x, Center.Y + y);
    }
}
