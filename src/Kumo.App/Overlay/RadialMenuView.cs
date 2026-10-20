using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Kumo.App.Theming;
using Kumo.Core.Radial;

namespace Kumo.App.Overlay;

/// <summary>Renders the ring of segments around the hub and turns pointer and keyboard input into item choices.</summary>
internal sealed class RadialMenuView : Canvas
{
    private static readonly Duration TransitionDuration = TimeSpan.FromMilliseconds(140);

    private readonly Ellipse _disk = new() { IsHitTestVisible = true };
    private readonly Canvas _segmentLayer = new();
    private readonly ScaleTransform _segmentScale = new(1, 1);
    private readonly List<RadialSegment> _segments = [];
    private RadialLayout _layout;
    private Palette _palette;
    private int _highlighted = -1;

    public RadialMenuView(RadialLayout layout, Palette palette)
    {
        _layout = layout;
        _palette = palette;
        Hub = new RadialHub(layout, palette);
        Hub.Click += (_, _) => HubInvoked?.Invoke();

        _segmentLayer.RenderTransform = _segmentScale;
        Children.Add(_disk);
        Children.Add(_segmentLayer);
        Children.Add(Hub);
        Restyle(layout, palette);

        MouseMove += (_, e) => Highlight(_layout.HitTest(e.GetPosition(this), _segments.Count));
        MouseLeave += (_, _) => Highlight(RadialHit.Outside);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event Action<int>? SegmentInvoked;

    public event Action? HubInvoked;

    public event Action? BackRequested;

    public RadialHub Hub { get; }

    public RadialLayout Layout => _layout;

    public bool ReducedMotion { get; set; }

    public int Count => _segments.Count;

    public RadialHit HitTest(Point point) => _layout.HitTest(point, _segments.Count);

    /// <summary>Whether the point is on the hub or the ring's disk, whether or not segments are shown yet.</summary>
    public bool IsOverMenu(Point point) => _layout.HitTest(point, 1).Kind != RadialHitKind.Outside;

    public void Restyle(RadialLayout layout, Palette palette)
    {
        _layout = layout;
        _palette = palette;
        Width = layout.Width;
        Height = layout.Height;
        double diskRadius = layout.OuterRadius + 5;
        _disk.Width = _disk.Height = diskRadius * 2;
        _disk.Fill = palette.Surface;
        _disk.Stroke = palette.SurfaceBorder;
        _disk.StrokeThickness = 1;
        _disk.Effect = palette.IsHighContrast ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Color = palette.Shadow, Opacity = 1 };
        SetLeft(_disk, layout.Center.X - diskRadius);
        SetTop(_disk, layout.Center.Y - diskRadius);
        _segmentScale.CenterX = layout.Center.X;
        _segmentScale.CenterY = layout.Center.Y;
        Hub.Restyle(layout, palette);
    }

    public void ShowItems(IReadOnlyList<RadialItem> items, bool showIcons, bool animate)
    {
        _segmentLayer.Children.Clear();
        _segments.Clear();
        _highlighted = -1;
        for (int i = 0; i < items.Count; i++)
        {
            var segment = new RadialSegment(items[i], i, items.Count, _layout, _palette, showIcons);
            int index = i;
            segment.Click += (_, _) => SegmentInvoked?.Invoke(index);
            _segments.Add(segment);
            _segmentLayer.Children.Add(segment);
        }
        SetRingVisible(true);
        if (animate && !ReducedMotion)
            AnimateIn();
    }

    public void SetRingVisible(bool visible)
    {
        _disk.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        _segmentLayer.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        if (!visible)
            Highlight(RadialHit.Outside);
    }

    public void ClearItems()
    {
        _segmentLayer.Children.Clear();
        _segments.Clear();
        _highlighted = -1;
    }

    public RadialHit Highlight(RadialHit hit)
    {
        int index = hit.Kind == RadialHitKind.Segment ? hit.Index : -1;
        Hub.IsHighlighted = hit.Kind == RadialHitKind.Hub;
        if (index == _highlighted)
            return hit;
        _highlighted = index;
        for (int i = 0; i < _segments.Count; i++)
            _segments[i].IsHighlighted = i == index;
        HighlightChanged?.Invoke(index >= 0 ? _segments[index].Item : null);
        return hit;
    }

    public event Action<RadialItem?>? HighlightChanged;

    public void FocusSegment(int index)
    {
        if (index >= 0 && index < _segments.Count)
            Keyboard.Focus(_segments[index]);
        else if (_segments.Count == 0)
            Keyboard.Focus(Hub);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        int focused = _segments.FindIndex(s => s.IsKeyboardFocused);
        switch (e.Key)
        {
            case Key.Right or Key.Down:
                FocusSegment(RadialGeometry.Step(focused, _segments.Count, 1));
                e.Handled = true;
                break;
            case Key.Left or Key.Up:
                FocusSegment(RadialGeometry.Step(focused, _segments.Count, -1));
                e.Handled = true;
                break;
            case Key.Home:
                FocusSegment(0);
                e.Handled = true;
                break;
            case Key.End:
                FocusSegment(_segments.Count - 1);
                e.Handled = true;
                break;
            case Key.Back:
                BackRequested?.Invoke();
                e.Handled = true;
                break;
        }
        if (e.Handled && focused != _segments.FindIndex(s => s.IsKeyboardFocused))
        {
            int now = _segments.FindIndex(s => s.IsKeyboardFocused);
            HighlightChanged?.Invoke(now >= 0 ? _segments[now].Item : null);
        }
    }

    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _segmentLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TransitionDuration) { EasingFunction = ease });
        _segmentScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TransitionDuration) { EasingFunction = ease });
        _segmentScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TransitionDuration) { EasingFunction = ease });
    }
}
