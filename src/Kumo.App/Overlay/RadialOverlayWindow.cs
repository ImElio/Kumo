using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Kumo.Core.Radial;
using Kumo.Windows.Input;
using Kumo.Windows.Interop;

namespace Kumo.App.Overlay;

internal sealed class RadialOverlayWindow : Window
{
    private const double Size = 340;
    private const double OuterRadius = 160;
    private const double InnerRadius = 62;

    private static readonly string[] Categories = ["Convert", "Compress", "Edit", "Extract", "Merge", "Split", "Protect", "More"];

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly List<Path> _segments = [];
    private readonly TextBlock _status = new();
    private int _highlighted = -1;
    private IReadOnlyList<string> _heldFiles = [];

    public RadialOverlayWindow()
    {
        Title = "Kumo";
        Width = Height = Size;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Content = _canvas;
        BuildVisuals();

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => Highlight(-1);
        Drop += OnDrop;
        MouseMove += (_, e) => Highlight(ClickHit(e.GetPosition(_canvas)));
        MouseLeftButtonUp += OnClick;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Hide();
        };
        SourceInitialized += (_, _) => OverlayWindowInterop.ApplyOverlayStyles(Handle);
    }

    public event Action<IReadOnlyList<string>, string>? FilesDropped;

    public bool DropReceived { get; private set; }

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    public bool IsHoldingFiles => _heldFiles.Count > 0 && IsVisible;

    public void ShowForFiles(IReadOnlyList<string> files, ScreenPoint point)
    {
        ShowAt(point);
        _heldFiles = files;
        _status.Text = $"{files.Count} file(s)";
        OverlayWindowInterop.SetActivatable(Handle, true);
        Activate();
    }

    public void ShowAt(ScreenPoint point)
    {
        _heldFiles = [];
        OverlayWindowInterop.SetActivatable(Handle, false);
        DropReceived = false;
        Highlight(-1);
        _status.Text = "Drop on an action";
        OverlayWindowInterop.MoveOnto(Handle, point);
        if (!IsVisible)
            Show();
        OverlayWindowInterop.CenterOn(Handle, point);
    }

    private void BuildVisuals()
    {
        var disk = new Ellipse
        {
            Width = OuterRadius * 2 + 8,
            Height = OuterRadius * 2 + 8,
            Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0xFA, 0xF8, 0xF5)),
        };
        Canvas.SetLeft(disk, Size / 2 - OuterRadius - 4);
        Canvas.SetTop(disk, Size / 2 - OuterRadius - 4);
        _canvas.Children.Add(disk);

        for (int i = 0; i < Categories.Length; i++)
        {
            var (start, end) = RadialGeometry.SegmentBounds(i, Categories.Length, 3);
            var segment = new Path
            {
                Data = SegmentGeometry(start, end),
                Fill = new SolidColorBrush(Color.FromRgb(0xEE, 0xEA, 0xE4)),
            };
            _segments.Add(segment);
            _canvas.Children.Add(segment);

            var (x, y) = RadialGeometry.PointAt((OuterRadius + InnerRadius) / 2, RadialGeometry.CenterAngle(i, Categories.Length));
            var label = new TextBlock { Text = Categories[i], FontSize = 12, Width = 80, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
            Canvas.SetLeft(label, Size / 2 + x - 40);
            Canvas.SetTop(label, Size / 2 + y - 8);
            _canvas.Children.Add(label);
        }

        _status.Width = InnerRadius * 2;
        _status.TextAlignment = TextAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.FontSize = 11;
        Canvas.SetLeft(_status, Size / 2 - InnerRadius);
        Canvas.SetTop(_status, Size / 2 - 14);
        _canvas.Children.Add(_status);
    }

    private static Geometry SegmentGeometry(double start, double end)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(InnerRadius, start), true, true);
            context.LineTo(At(OuterRadius, start), true, false);
            context.ArcTo(At(OuterRadius, end), new System.Windows.Size(OuterRadius, OuterRadius), 0, end - start > 180, SweepDirection.Clockwise, true, false);
            context.LineTo(At(InnerRadius, end), true, false);
            context.ArcTo(At(InnerRadius, start), new System.Windows.Size(InnerRadius, InnerRadius), 0, end - start > 180, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point At(double radius, double angle)
    {
        var (x, y) = RadialGeometry.PointAt(radius, angle);
        return new Point(Size / 2 + x, Size / 2 + y);
    }

    private RadialHit HitTest(DragEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        return RadialGeometry.HitTest(p.X - Size / 2, p.Y - Size / 2, Categories.Length, InnerRadius, OuterRadius);
    }

    private int ClickHit(Point p)
    {
        if (_heldFiles.Count == 0)
            return -1;
        var hit = RadialGeometry.HitTest(p.X - Size / 2, p.Y - Size / 2, Categories.Length, InnerRadius, OuterRadius);
        return hit.Kind == RadialHitKind.Segment ? hit.Index : -1;
    }

    private void OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        int index = ClickHit(e.GetPosition(_canvas));
        if (index < 0)
            return;
        var files = _heldFiles;
        _heldFiles = [];
        FilesDropped?.Invoke(files, Categories[index]);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        var hit = HitTest(e);
        Highlight(hasFiles && hit.Kind == RadialHitKind.Segment ? hit.Index : -1);
        e.Effects = hasFiles && hit.Kind == RadialHitKind.Segment ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var hit = HitTest(e);
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        e.Effects = hit.Kind == RadialHitKind.Segment && files.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        DropReceived = true;
        if (e.Effects == DragDropEffects.Copy)
            FilesDropped?.Invoke(files, Categories[hit.Index]);
    }

    private void Highlight(int index)
    {
        if (index == _highlighted)
            return;
        _highlighted = index;
        for (int i = 0; i < _segments.Count; i++)
            _segments[i].Fill = new SolidColorBrush(i == index ? Color.FromRgb(0xF2, 0x8C, 0x38) : Color.FromRgb(0xEE, 0xEA, 0xE4));
        _status.Text = index >= 0 ? Categories[index] : "Drop on an action";
    }
}
