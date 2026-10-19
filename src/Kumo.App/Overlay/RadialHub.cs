using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Kumo.App.Mascot;
using Kumo.App.Theming;

namespace Kumo.App.Overlay;

/// <summary>The centre of the menu: Kumo itself, a determinate progress ring while working, and "Back" in submenus.</summary>
internal sealed class RadialHub : Button
{
    public static readonly DependencyProperty MascotStateProperty = DependencyProperty.Register(
        nameof(MascotState), typeof(MascotState), typeof(RadialHub), new FrameworkPropertyMetadata(MascotState.Idle, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double?), typeof(RadialHub), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowsBackProperty = DependencyProperty.Register(
        nameof(ShowsBack), typeof(bool), typeof(RadialHub), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnShowsBackChanged));

    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(RadialHub), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface CaptionFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private RadialLayout _layout;
    private Palette _palette;

    static RadialHub()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RadialHub), new FrameworkPropertyMetadata(typeof(RadialHub)));
        FocusVisualStyleProperty.OverrideMetadata(typeof(RadialHub), new FrameworkPropertyMetadata(null));
    }

    public RadialHub(RadialLayout layout, Palette palette)
    {
        _layout = layout;
        _palette = palette;
        AutomationProperties.SetName(this, "Close");
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public MascotState MascotState
    {
        get => (MascotState)GetValue(MascotStateProperty);
        set => SetValue(MascotStateProperty, value);
    }

    /// <summary>Fraction completed, or null when no work is in progress.</summary>
    public double? Progress
    {
        get => (double?)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool ShowsBack
    {
        get => (bool)GetValue(ShowsBackProperty);
        set => SetValue(ShowsBackProperty, value);
    }

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    public void Restyle(RadialLayout layout, Palette palette)
    {
        _layout = layout;
        _palette = palette;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusedChanged(e);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size constraint) => new(_layout.Width, _layout.Height);

    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        (hitTestParameters.HitPoint - _layout.Center).Length <= _layout.HubRadius ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    protected override void OnRender(DrawingContext context)
    {
        var center = _layout.Center;
        double radius = _layout.HubRadius;
        bool active = (IsHighlighted || IsPressed) && ShowsBack;
        context.DrawEllipse(active ? _palette.SegmentHover : _palette.Surface, new Pen(_palette.SurfaceBorder, 1), center, radius, radius);

        if (Progress is { } progress)
        {
            double ringRadius = radius - 3;
            var track = new Pen(_palette.ProgressTrack, 4);
            context.DrawEllipse(null, track, center, ringRadius, ringRadius);
            if (progress > 0)
                context.DrawGeometry(null, new Pen(_palette.Progress, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Arc(center, ringRadius, Math.Clamp(progress, 0, 1)));
        }

        if (IsKeyboardFocused)
            context.DrawEllipse(null, new Pen(_palette.Focus, 2), center, radius - 2, radius - 2);

        double mascotSize = radius * (ShowsBack ? 1.15 : 1.35);
        double mascotTop = center.Y - mascotSize / 2 - (ShowsBack ? radius * 0.16 : 0);
        context.DrawImage(MascotImages.For(MascotState), new Rect(center.X - mascotSize / 2, mascotTop, mascotSize, mascotSize));

        if (ShowsBack)
        {
            var caption = new FormattedText("‹ Back", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, CaptionFace,
                10.5 * _layout.Scale, _palette.MutedText, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(caption, new Point(center.X - caption.Width / 2, mascotTop + mascotSize - 4 * _layout.Scale));
        }
    }

    private static Geometry Arc(Point center, double radius, double fraction)
    {
        if (fraction >= 0.999)
            return new EllipseGeometry(center, radius, radius);
        double angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static void OnShowsBackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        AutomationProperties.SetName(d, (bool)e.NewValue ? "Back" : "Close");
}
