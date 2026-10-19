using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;

namespace Kumo.App.Mascot;

/// <summary>Draws Kumo directly with a DrawingContext; changing state swaps a cached frozen drawing.</summary>
public sealed class KumoMascot : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(MascotState), typeof(KumoMascot),
        new FrameworkPropertyMetadata(MascotState.Idle, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public KumoMascot()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetName(this, "Kumo");
    }

    public MascotState State
    {
        get => (MascotState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = Math.Min(
            double.IsInfinity(availableSize.Width) ? MascotArt.DesignSize : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? MascotArt.DesignSize : availableSize.Height);
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext context)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0)
            return;
        double scale = side / MascotArt.DesignSize;
        context.PushTransform(new TranslateTransform((ActualWidth - side) / 2, (ActualHeight - side) / 2));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawDrawing(MascotArt.Drawing(State));
        context.Pop();
        context.Pop();
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        AutomationProperties.SetHelpText(d, e.NewValue?.ToString() ?? string.Empty);
}
