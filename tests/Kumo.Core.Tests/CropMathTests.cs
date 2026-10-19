using Kumo.Core.Imaging;

namespace Kumo.Core.Tests;

public class CropMathTests
{
    private const double W = 400;
    private const double H = 200;

    [Fact]
    public void Initial_frame_is_the_whole_image_or_the_largest_centred_ratio()
    {
        Assert.Equal(new CropRect(0, 0, W, H), CropMath.Initial(W, H, null));
        Assert.Equal(new CropRect(100, 0, 200, 200), CropMath.Initial(W, H, 1));
    }

    [Fact]
    public void Free_corner_drag_moves_only_that_corner()
    {
        var rect = CropMath.Drag(new CropRect(0, 0, W, H), CropHandle.TopLeft, 50, 20, W, H, null);
        Assert.Equal(new CropRect(50, 20, 350, 180), rect);
    }

    [Fact]
    public void Edges_cannot_leave_the_image_or_cross_each_other()
    {
        var start = new CropRect(100, 50, 100, 100);
        Assert.Equal(W, CropMath.Drag(start, CropHandle.Right, 500, 0, W, H, null).Right);
        Assert.Equal(CropMath.MinimumSize, CropMath.Drag(start, CropHandle.Right, -500, 0, W, H, null).Width);
        Assert.Equal(0, CropMath.Drag(start, CropHandle.Top, 0, -500, W, H, null).Y);
    }

    [Fact]
    public void Moving_keeps_the_size_and_stays_inside()
    {
        var rect = CropMath.Drag(new CropRect(100, 50, 100, 100), CropHandle.Move, 1000, -1000, W, H, null);
        Assert.Equal(new CropRect(300, 0, 100, 100), rect);
    }

    [Theory]
    [InlineData(CropHandle.BottomRight, 30, 0)]
    [InlineData(CropHandle.TopLeft, -10, -40)]
    [InlineData(CropHandle.Right, 25, 0)]
    [InlineData(CropHandle.Bottom, 0, 25)]
    public void Locked_ratio_is_preserved_by_every_handle(CropHandle handle, double dx, double dy)
    {
        var start = new CropRect(100, 50, 100, 100);
        var rect = CropMath.Drag(start, handle, dx, dy, W, H, 1);
        Assert.Equal(rect.Width, rect.Height, 6);
        Assert.InRange(rect.X, 0, W);
        Assert.InRange(rect.Bottom, 0, H);
    }

    [Fact]
    public void Locked_corner_drag_is_limited_by_the_image()
    {
        var rect = CropMath.Drag(new CropRect(100, 50, 100, 100), CropHandle.BottomRight, 500, 500, W, H, 1);
        Assert.Equal(150, rect.Width, 6);
        Assert.Equal(H, rect.Bottom, 6);
    }

    [Fact]
    public void Applying_a_ratio_keeps_the_centre_and_fits_the_image()
    {
        var rect = CropMath.ApplyRatio(new CropRect(0, 0, W, H), W, H, 16.0 / 9);
        Assert.Equal(16.0 / 9, rect.Width / rect.Height, 6);
        Assert.Equal(W / 2, rect.CenterX, 6);
        Assert.InRange(rect.Bottom, 0, H);
    }

    [Fact]
    public void Pixels_are_rounded_and_clamped()
    {
        Assert.Equal((10, 5, 30, 15), CropRect.ToPixels(new CropRect(0.1, 0.1, 0.3, 0.3), 100, 50));
        Assert.Equal((99, 49, 1, 1), CropRect.ToPixels(new CropRect(1, 1, 0, 0), 100, 50));
    }
}
