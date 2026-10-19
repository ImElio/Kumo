using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Kumo.App.Theming;
using Kumo.Core.Radial;

namespace Kumo.App.Overlay;

/// <summary>
/// One wedge of the ring. It is a real Button so it takes keyboard focus and exposes the Invoke pattern to screen
/// readers, but it has no template: it draws itself and only hit-tests inside its own wedge.
/// </summary>
internal sealed class RadialSegment : Button
{
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(RadialSegment), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly RadialLayout _layout;
    private readonly Palette _palette;
    private readonly bool _showIcon;
    private readonly Geometry _wedge;
    private readonly double _centerAngle;
    private readonly double _sweep;

    static RadialSegment()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RadialSegment), new FrameworkPropertyMetadata(typeof(RadialSegment)));
        FocusVisualStyleProperty.OverrideMetadata(typeof(RadialSegment), new FrameworkPropertyMetadata(null));
    }

    public RadialSegment(RadialItem item, int index, int count, RadialLayout layout, Palette palette, bool showIcon)
    {
        Item = item;
        Index = index;
        _layout = layout;
        _palette = palette;
        _showIcon = showIcon;
        _sweep = RadialGeometry.Sweep(count);
        _centerAngle = RadialGeometry.CenterAngle(index, count);
        var (start, end) = RadialGeometry.SegmentBounds(index, count, count == 1 ? 0 : RadialLayout.GapDegrees);
        _wedge = count == 1 ? Ring(layout) : Wedge(layout, start, end);

        Width = layout.Width;
        Height = layout.Height;
        AutomationProperties.SetName(this, item.OpensSubmenu ? $"{item.Label}, submenu" : item.Description);
        AutomationProperties.SetHelpText(this, item.OpensSubmenu ? item.Description : string.Empty);
        AutomationProperties.SetPositionInSet(this, index + 1);
        AutomationProperties.SetSizeOfSet(this, count);
    }

    public RadialItem Item { get; }

    public int Index { get; }

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusedChanged(e);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size constraint) => new(_layout.Width, _layout.Height);

    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        _wedge.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    protected override void OnRender(DrawingContext context)
    {
        bool active = IsHighlighted || IsPressed;
        var fill = active ? _palette.SegmentSelected : _palette.Segment;
        var foreground = active ? _palette.SelectedText : _palette.Text;
        Pen? outline = _palette.IsHighContrast ? new Pen(_palette.Text, 1) : null;
        context.DrawGeometry(fill, outline, _wedge);

        if (IsKeyboardFocused)
            context.DrawGeometry(null, new Pen(_palette.Focus, 2) { LineJoin = PenLineJoin.Round }, InsetWedge());

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double scale = _layout.Scale;
        var anchor = _layout.At(_layout.ContentRadius, _centerAngle);
        double chord = 2 * _layout.ContentRadius * Math.Sin(Math.Min(Math.PI / 2, _sweep * Math.PI / 360));
        double maxWidth = Math.Clamp(chord * 0.82, 52 * scale, (_layout.OuterRadius - _layout.InnerRadius) * 1.25);

        var label = new FormattedText(Item.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace,
            (_showIcon ? 12 : 14.5) * scale, foreground, dpi)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 2,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        if (_showIcon)
        {
            double iconSize = 20 * scale;
            double blockHeight = iconSize + 4 * scale + label.Height;
            double top = anchor.Y - blockHeight / 2;
            double iconScale = iconSize / RadialIcons.GridSize;
            var pen = new Pen(foreground, 1.7 / iconScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            context.PushTransform(new TranslateTransform(anchor.X - iconSize / 2, top));
            context.PushTransform(new ScaleTransform(iconScale, iconScale));
            context.DrawGeometry(null, pen, RadialIcons.For(Item.Category));
            context.Pop();
            context.Pop();
            context.DrawText(label, new Point(anchor.X - maxWidth / 2, top + iconSize + 4 * scale));
        }
        else
        {
            context.DrawText(label, new Point(anchor.X - maxWidth / 2, anchor.Y - label.Height / 2));
        }
    }

    private Geometry InsetWedge()
    {
        if (_sweep >= 360)
            return Ring(_layout, 2);
        var (start, end) = RadialGeometry.SegmentBounds(Index, (int)Math.Round(360 / _sweep), RadialLayout.GapDegrees + 1.2);
        return Wedge(_layout, start, end, 2);
    }

    private static Geometry Wedge(RadialLayout layout, double start, double end, double inset = 0)
    {
        double outer = layout.OuterRadius - inset;
        double inner = layout.InnerRadius + inset;
        bool large = end - start > 180;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(layout.At(inner, start), true, true);
            context.LineTo(layout.At(outer, start), true, true);
            context.ArcTo(layout.At(outer, end), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, true);
            context.LineTo(layout.At(inner, end), true, true);
            context.ArcTo(layout.At(inner, start), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Ring(RadialLayout layout, double inset = 0)
    {
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(layout.Center, layout.OuterRadius - inset, layout.OuterRadius - inset),
            new EllipseGeometry(layout.Center, layout.InnerRadius + inset, layout.InnerRadius + inset));
        ring.Freeze();
        return ring;
    }
}
