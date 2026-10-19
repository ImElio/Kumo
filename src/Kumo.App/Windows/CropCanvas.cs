using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Kumo.Core.Imaging;

namespace Kumo.App.Windows;

/// <summary>
/// Draws the crop frame over the picture and turns pointer and arrow-key input into frame changes. Only this
/// element re-renders while dragging; the picture underneath never changes, which keeps dragging smooth.
/// </summary>
internal sealed class CropCanvas : FrameworkElement
{
    private const double HandleReach = 12;
    private const double HandleLength = 16;

    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x9A, 0, 0, 0)));
    private static readonly Pen FramePen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen HandlePen = Frozen(new Pen(Brushes.White, 4) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square });

    private readonly double _imageWidth;
    private readonly double _imageHeight;
    private readonly double _scale;
    private CropRect _rect;
    private CropRect _dragStart;
    private CropHandle? _dragHandle;
    private Point _pressPoint;

    public CropCanvas(double imageWidth, double imageHeight, double scale)
    {
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        _scale = scale;
        _rect = new CropRect(0, 0, imageWidth, imageHeight);
        Width = imageWidth * scale;
        Height = imageHeight * scale;
        Focusable = true;
        FocusVisualStyle = null;
        AutomationProperties.SetName(this, "Crop area. Use the arrow keys to move it.");
    }

    public event Action? CropChanged;

    public double? Ratio { get; set; }

    public CropRect Crop
    {
        get => _rect;
        set
        {
            _rect = value;
            InvalidateVisual();
            CropChanged?.Invoke();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        var frame = ToScreen(_rect);
        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(new Rect(0, 0, Width, Height)));
        dim.Children.Add(new RectangleGeometry(frame));
        context.DrawGeometry(Dim, null, dim);

        for (int i = 1; i < 3; i++)
        {
            double x = frame.Left + frame.Width * i / 3;
            double y = frame.Top + frame.Height * i / 3;
            context.DrawLine(GridPen, new Point(x, frame.Top), new Point(x, frame.Bottom));
            context.DrawLine(GridPen, new Point(frame.Left, y), new Point(frame.Right, y));
        }
        context.DrawRectangle(null, FramePen, frame);
        if (IsKeyboardFocused)
            context.DrawRectangle(null, new Pen(SystemColors.HighlightBrush, 2), Rect.Inflate(frame, 3, 3));

        double length = Math.Min(HandleLength, Math.Min(frame.Width, frame.Height) / 3);
        foreach (var (corner, sx, sy) in new[] { (frame.TopLeft, 1, 1), (frame.TopRight, -1, 1), (frame.BottomLeft, 1, -1), (frame.BottomRight, -1, -1) })
        {
            context.DrawLine(HandlePen, corner, new Point(corner.X + sx * length, corner.Y));
            context.DrawLine(HandlePen, corner, new Point(corner.X, corner.Y + sy * length));
        }
        double midX = frame.Left + frame.Width / 2, midY = frame.Top + frame.Height / 2;
        context.DrawLine(HandlePen, new Point(midX - length / 2, frame.Top), new Point(midX + length / 2, frame.Top));
        context.DrawLine(HandlePen, new Point(midX - length / 2, frame.Bottom), new Point(midX + length / 2, frame.Bottom));
        context.DrawLine(HandlePen, new Point(frame.Left, midY - length / 2), new Point(frame.Left, midY + length / 2));
        context.DrawLine(HandlePen, new Point(frame.Right, midY - length / 2), new Point(frame.Right, midY + length / 2));
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _pressPoint = e.GetPosition(this);
        var handle = HandleAt(_pressPoint);
        if (handle == null)
        {
            // Pressing outside the frame starts a new one from that point.
            var start = ToImage(_pressPoint);
            _rect = new CropRect(start.X, start.Y, 0, 0);
            handle = CropHandle.BottomRight;
        }
        _dragStart = _rect;
        _dragHandle = handle;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_dragHandle is not { } handle)
        {
            Cursor = CursorFor(HandleAt(point));
            return;
        }
        var delta = point - _pressPoint;
        Crop = CropMath.Drag(_dragStart, handle, delta.X / _scale, delta.Y / _scale, _imageWidth, _imageHeight, Ratio);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragHandle == null)
            return;
        _dragHandle = null;
        ReleaseMouseCapture();
        if (_rect.Width < CropMath.MinimumSize || _rect.Height < CropMath.MinimumSize)
            Crop = CropMath.Initial(_imageWidth, _imageHeight, Ratio);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1) / _scale;
        (double dx, double dy) = e.Key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            Key.Down => (0.0, step),
            _ => (0.0, 0.0),
        };
        if (dx == 0 && dy == 0)
            return;
        Crop = CropMath.Drag(_rect, CropHandle.Move, dx, dy, _imageWidth, _imageHeight, Ratio);
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private CropHandle? HandleAt(Point point)
    {
        var frame = ToScreen(_rect);
        bool nearLeft = Math.Abs(point.X - frame.Left) <= HandleReach;
        bool nearRight = Math.Abs(point.X - frame.Right) <= HandleReach;
        bool nearTop = Math.Abs(point.Y - frame.Top) <= HandleReach;
        bool nearBottom = Math.Abs(point.Y - frame.Bottom) <= HandleReach;
        bool withinX = point.X >= frame.Left - HandleReach && point.X <= frame.Right + HandleReach;
        bool withinY = point.Y >= frame.Top - HandleReach && point.Y <= frame.Bottom + HandleReach;

        if (nearTop && nearLeft) return CropHandle.TopLeft;
        if (nearTop && nearRight) return CropHandle.TopRight;
        if (nearBottom && nearLeft) return CropHandle.BottomLeft;
        if (nearBottom && nearRight) return CropHandle.BottomRight;
        if (nearLeft && withinY) return CropHandle.Left;
        if (nearRight && withinY) return CropHandle.Right;
        if (nearTop && withinX) return CropHandle.Top;
        if (nearBottom && withinX) return CropHandle.Bottom;
        return frame.Contains(point) ? CropHandle.Move : null;
    }

    private static Cursor CursorFor(CropHandle? handle) => handle switch
    {
        CropHandle.Move => Cursors.SizeAll,
        CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
        CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
        CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
        CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    private Rect ToScreen(CropRect rect) => new(rect.X * _scale, rect.Y * _scale, Math.Max(0, rect.Width * _scale), Math.Max(0, rect.Height * _scale));

    private Point ToImage(Point point) =>
        new(Math.Clamp(point.X / _scale, 0, _imageWidth), Math.Clamp(point.Y / _scale, 0, _imageHeight));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
