using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Kumo.App.Theming;
using Kumo.Core.Radial;
using Kumo.Windows.Input;
using Kumo.Windows.Interop;

namespace Kumo.App.Overlay;

internal sealed record StatusAction(string Label, Action Invoke);

/// <summary>
/// The transparent, topmost window hosting the radial menu and its status bar. It is a registered OLE drop target
/// (AllowDrop), never takes activation while a drag is in progress, and only becomes activatable when Kumo owns the
/// interaction (keyboard or click mode).
/// </summary>
internal sealed class RadialOverlayWindow : Window
{
    private readonly Grid _root = new();
    private readonly Border _status = new();
    private readonly TextBlock _statusText = new();
    private readonly StackPanel _statusActions = new() { Orientation = Orientation.Horizontal };
    private Palette _palette;

    public RadialOverlayWindow(RadialLayout layout, Palette palette)
    {
        _palette = palette;
        Title = "Kumo";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        Menu = new RadialMenuView(layout, palette);
        _root.Children.Add(Menu);

        _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.FontSize = 13;
        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Polite);
        var statusContent = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_statusActions, Dock.Right);
        statusContent.Children.Add(_statusActions);
        statusContent.Children.Add(_statusText);
        _status.Child = statusContent;
        _status.CornerRadius = new CornerRadius(16);
        _status.Padding = new Thickness(14, 7, 8, 7);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Bottom;
        _status.BorderThickness = new Thickness(1);
        _root.Children.Add(_status);
        Content = _root;
        ApplyLayout(layout, palette);

        SourceInitialized += (_, _) =>
        {
            OverlayWindowInterop.ApplyOverlayStyles(Handle);
            // The overlay is small and static, so software rendering costs nothing visible and spares a Direct3D
            // device whose buffers would stay committed while Kumo idles. Editor windows keep hardware rendering
            // so dragging stays smooth.
            if (HwndSource.FromHwnd(Handle)?.CompositionTarget is { } target)
                target.RenderMode = RenderMode.SoftwareOnly;
        };
    }

    public RadialMenuView Menu { get; }

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    public bool IsInteractive { get; private set; }

    public void ApplyLayout(RadialLayout layout, Palette palette)
    {
        _palette = palette;
        Menu.Restyle(layout, palette);
        _status.Background = palette.Surface;
        _status.BorderBrush = palette.SurfaceBorder;
        _statusText.Foreground = palette.Text;
        _status.MaxWidth = layout.Width - 8;
        _status.Margin = new Thickness(0, 0, 0, layout.StatusHeight - 44);
    }

    /// <summary>Shows the window centred on a screen point, on the monitor and DPI of that point.</summary>
    public void ShowAt(ScreenPoint point)
    {
        OverlayWindowInterop.MoveOnto(Handle, point);
        if (!IsVisible)
            Show();
        UpdateLayout();
        OverlayWindowInterop.CenterOn(Handle, point);
    }

    /// <returns>Whether the window became the foreground window; Windows may refuse while another app holds it.</returns>
    public bool SetInteractive(bool interactive)
    {
        IsInteractive = interactive;
        OverlayWindowInterop.SetActivatable(Handle, interactive);
        if (!interactive || !IsVisible)
            return false;
        bool foreground = OverlayWindowInterop.TryBringToFront(Handle);
        Activate();
        return foreground;
    }

    public void SetStatus(string text, params StatusAction[] actions)
    {
        _statusText.Text = text;
        AutomationProperties.SetName(_statusText, text);
        _statusActions.Children.Clear();
        foreach (var action in actions)
        {
            var button = new Button
            {
                Content = action.Label,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
                Foreground = _palette.Text,
                Template = StatusButtonTemplate(),
                Cursor = Cursors.Hand,
            };
            button.Click += (_, _) => action.Invoke();
            _statusActions.Children.Add(button);
        }
        _status.Visibility = text.Length == 0 && actions.Length == 0 ? Visibility.Hidden : Visibility.Visible;
        if (IsVisible)
            UIElementAutomationPeer.CreatePeerForElement(_statusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public bool ContainsScreenPoint(ScreenPoint point)
    {
        if (!IsVisible || !OverlayWindowInterop.GetBounds(Handle).Contains(point))
            return false;
        var local = PointFromScreen(new Point(point.X, point.Y));
        if (Menu.HitTest(local).Kind == RadialHitKind.Hub)
            return true;
        if (Menu.HitTest(local).Kind == RadialHitKind.Segment && Menu.Count > 0)
            return true;
        return _status.IsVisible && new Rect(_status.TranslatePoint(new Point(), this), _status.RenderSize).Contains(local);
    }

    private ControlTemplate StatusButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, _palette.Segment);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(Border.BorderBrushProperty, _palette.Focus);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, _palette.SegmentHover, "Chrome"));
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
        template.Triggers.Add(hover);
        template.Triggers.Add(focus);
        return template;
    }
}
