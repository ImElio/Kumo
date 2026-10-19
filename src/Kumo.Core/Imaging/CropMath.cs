namespace Kumo.Core.Imaging;

/// <summary>A rectangle in image pixels (fractional while dragging).</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    /// <summary>The same rectangle as fractions of the image, so it can be applied to images of other sizes.</summary>
    public CropRect Normalize(double imageWidth, double imageHeight) =>
        new(X / imageWidth, Y / imageHeight, Width / imageWidth, Height / imageHeight);

    /// <summary>Whole-pixel rectangle for a fractional region, always inside the image and at least 1×1.</summary>
    public static (int X, int Y, int Width, int Height) ToPixels(CropRect fraction, int imageWidth, int imageHeight)
    {
        int left = Math.Clamp((int)Math.Round(fraction.X * imageWidth), 0, imageWidth - 1);
        int top = Math.Clamp((int)Math.Round(fraction.Y * imageHeight), 0, imageHeight - 1);
        int right = Math.Clamp((int)Math.Round(fraction.Right * imageWidth), left + 1, imageWidth);
        int bottom = Math.Clamp((int)Math.Round(fraction.Bottom * imageHeight), top + 1, imageHeight);
        return (left, top, right - left, bottom - top);
    }
}

public enum CropHandle
{
    Move,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Geometry of the crop frame, kept free of UI types so the dragging rules are testable. All values are in image
/// pixels; <c>ratio</c> is width divided by height, or null for a free crop.
/// </summary>
public static class CropMath
{
    public const double MinimumSize = 8;

    /// <summary>The largest centred rectangle with the given ratio, or the whole image.</summary>
    public static CropRect Initial(double width, double height, double? ratio)
    {
        if (ratio is not { } r)
            return new CropRect(0, 0, width, height);
        double w = width, h = width / r;
        if (h > height)
        {
            h = height;
            w = height * r;
        }
        return new CropRect((width - w) / 2, (height - h) / 2, w, h);
    }

    /// <summary>Re-shapes the frame to a new ratio around its current centre, keeping its height where possible.</summary>
    public static CropRect ApplyRatio(CropRect current, double width, double height, double? ratio)
    {
        if (ratio is not { } r)
            return current;
        double h = current.Height, w = h * r;
        if (w > width)
        {
            w = width;
            h = w / r;
        }
        if (h > height)
        {
            h = height;
            w = h * r;
        }
        return Shift(new CropRect(current.CenterX - w / 2, current.CenterY - h / 2, w, h), width, height);
    }

    /// <summary>The frame after dragging <paramref name="handle"/> by (dx, dy) from <paramref name="start"/>.</summary>
    public static CropRect Drag(CropRect start, CropHandle handle, double dx, double dy, double width, double height, double? ratio)
    {
        if (handle == CropHandle.Move)
            return Shift(start with { X = start.X + dx, Y = start.Y + dy }, width, height);
        return ratio is { } r ? DragLocked(start, handle, dx, dy, width, height, r) : DragFree(start, handle, dx, dy, width, height);
    }

    private static CropRect DragFree(CropRect s, CropHandle handle, double dx, double dy, double width, double height)
    {
        double left = s.X, top = s.Y, right = s.Right, bottom = s.Bottom;
        if (handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft)
            left = Math.Clamp(s.X + dx, 0, right - MinimumSize);
        if (handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight)
            right = Math.Clamp(s.Right + dx, left + MinimumSize, width);
        if (handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight)
            top = Math.Clamp(s.Y + dy, 0, bottom - MinimumSize);
        if (handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight)
            bottom = Math.Clamp(s.Bottom + dy, top + MinimumSize, height);
        return new CropRect(left, top, right - left, bottom - top);
    }

    private static CropRect DragLocked(CropRect s, CropHandle handle, double dx, double dy, double width, double height, double r)
    {
        double minWidth = Math.Max(MinimumSize, MinimumSize * r);
        switch (handle)
        {
            case CropHandle.Left or CropHandle.Right:
            {
                double w = handle == CropHandle.Right ? s.Width + dx : s.Width - dx;
                double maxW = handle == CropHandle.Right ? width - s.X : s.Right;
                w = Math.Clamp(w, minWidth, Math.Max(minWidth, Math.Min(maxW, height * r)));
                double h = w / r;
                double x = handle == CropHandle.Right ? s.X : s.Right - w;
                double y = Math.Clamp(s.CenterY - h / 2, 0, height - h);
                return new CropRect(x, y, w, h);
            }
            case CropHandle.Top or CropHandle.Bottom:
            {
                double h = handle == CropHandle.Bottom ? s.Height + dy : s.Height - dy;
                double maxH = handle == CropHandle.Bottom ? height - s.Y : s.Bottom;
                h = Math.Clamp(h, minWidth / r, Math.Max(minWidth / r, Math.Min(maxH, width / r)));
                double w = h * r;
                double y = handle == CropHandle.Bottom ? s.Y : s.Bottom - h;
                double x = Math.Clamp(s.CenterX - w / 2, 0, width - w);
                return new CropRect(x, y, w, h);
            }
            default:
            {
                bool east = handle is CropHandle.TopRight or CropHandle.BottomRight;
                bool south = handle is CropHandle.BottomLeft or CropHandle.BottomRight;
                double anchorX = east ? s.X : s.Right;
                double anchorY = south ? s.Y : s.Bottom;
                double wFromX = east ? s.Width + dx : s.Width - dx;
                double wFromY = (south ? s.Height + dy : s.Height - dy) * r;
                double w = Math.Abs(dx) >= Math.Abs(dy * r) ? wFromX : wFromY;
                double maxW = Math.Min(east ? width - anchorX : anchorX, (south ? height - anchorY : anchorY) * r);
                w = Math.Clamp(w, minWidth, Math.Max(minWidth, maxW));
                double h = w / r;
                return new CropRect(east ? anchorX : anchorX - w, south ? anchorY : anchorY - h, w, h);
            }
        }
    }

    private static CropRect Shift(CropRect rect, double width, double height) =>
        rect with
        {
            X = Math.Clamp(rect.X, 0, Math.Max(0, width - rect.Width)),
            Y = Math.Clamp(rect.Y, 0, Math.Max(0, height - rect.Height)),
        };
}
