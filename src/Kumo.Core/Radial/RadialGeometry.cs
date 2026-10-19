namespace Kumo.Core.Radial;

public enum RadialHitKind
{
    Outside,
    Hub,
    Segment,
}

public readonly record struct RadialHit(RadialHitKind Kind, int Index)
{
    public static RadialHit Outside => new(RadialHitKind.Outside, -1);
    public static RadialHit Hub => new(RadialHitKind.Hub, -1);
}

/// <summary>
/// Angles are measured in degrees clockwise from twelve o'clock, matching how the menu is read.
/// Segment 0 is centred on twelve o'clock.
/// </summary>
public static class RadialGeometry
{
    public static double Sweep(int count) => count <= 0 ? 0 : 360.0 / count;

    public static double CenterAngle(int index, int count) => index * Sweep(count);

    public static (double Start, double End) SegmentBounds(int index, int count, double gapDegrees)
    {
        double sweep = Sweep(count);
        double center = CenterAngle(index, count);
        double half = Math.Max(0, sweep / 2 - gapDegrees / 2);
        return (center - half, center + half);
    }

    /// <summary>Converts an offset from the centre (screen coordinates, y down) into a clockwise angle.</summary>
    public static double AngleOf(double dx, double dy)
    {
        double degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    public static (double X, double Y) PointAt(double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180;
        return (radius * Math.Sin(radians), -radius * Math.Cos(radians));
    }

    /// <summary>
    /// Gaps between segments are deliberately hit-tested as the nearest segment so a slightly imprecise drop
    /// still lands on an operation.
    /// </summary>
    public static RadialHit HitTest(double dx, double dy, int count, double innerRadius, double outerRadius)
    {
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= innerRadius)
            return RadialHit.Hub;
        if (distance > outerRadius || count <= 0)
            return RadialHit.Outside;

        double sweep = Sweep(count);
        int index = (int)Math.Floor((AngleOf(dx, dy) + sweep / 2) / sweep) % count;
        return new RadialHit(RadialHitKind.Segment, index);
    }

    /// <summary>Index of the neighbouring segment when moving focus with the keyboard.</summary>
    public static int Step(int index, int count, int delta)
    {
        if (count <= 0)
            return -1;
        if (index < 0)
            return delta >= 0 ? 0 : count - 1;
        return ((index + delta) % count + count) % count;
    }
}
