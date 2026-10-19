using Kumo.Core.Radial;

namespace Kumo.Core.Tests;

public class RadialGeometryTests
{
    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, 0, 90)]
    [InlineData(0, 1, 180)]
    [InlineData(-1, 0, 270)]
    public void Angles_run_clockwise_from_twelve_o_clock(double dx, double dy, double expected)
    {
        Assert.Equal(expected, RadialGeometry.AngleOf(dx, dy), 6);
    }

    [Fact]
    public void Point_at_angle_round_trips()
    {
        var (x, y) = RadialGeometry.PointAt(100, 135);
        Assert.Equal(135, RadialGeometry.AngleOf(x, y), 6);
        Assert.Equal(100, Math.Sqrt(x * x + y * y), 6);
    }

    [Fact]
    public void Hub_and_outside_are_distinguished_from_segments()
    {
        Assert.Equal(RadialHit.Hub, RadialGeometry.HitTest(5, 5, 8, 40, 100));
        Assert.Equal(RadialHit.Outside, RadialGeometry.HitTest(0, -101, 8, 40, 100));
        Assert.Equal(new RadialHit(RadialHitKind.Segment, 0), RadialGeometry.HitTest(0, -70, 8, 40, 100));
    }

    [Theory]
    [InlineData(4, 90, 1)]
    [InlineData(4, 44, 0)]
    [InlineData(4, 46, 1)]
    [InlineData(4, 359, 0)]
    [InlineData(4, 316, 0)]
    [InlineData(4, 314, 3)]
    [InlineData(3, 120, 1)]
    public void Segments_are_centred_on_their_angle(int count, double angle, int expected)
    {
        var (x, y) = RadialGeometry.PointAt(70, angle);
        var hit = RadialGeometry.HitTest(x, y, count, 40, 100);
        Assert.Equal(RadialHitKind.Segment, hit.Kind);
        Assert.Equal(expected, hit.Index);
    }

    [Fact]
    public void Segment_bounds_leave_the_requested_gap()
    {
        var (start, end) = RadialGeometry.SegmentBounds(1, 4, 4);
        Assert.Equal(47, start, 6);
        Assert.Equal(133, end, 6);
    }

    [Theory]
    [InlineData(0, 5, 1, 1)]
    [InlineData(4, 5, 1, 0)]
    [InlineData(0, 5, -1, 4)]
    [InlineData(-1, 5, 1, 0)]
    [InlineData(-1, 5, -1, 4)]
    [InlineData(2, 0, 1, -1)]
    public void Keyboard_steps_wrap_around(int index, int count, int delta, int expected)
    {
        Assert.Equal(expected, RadialGeometry.Step(index, count, delta));
    }
}
