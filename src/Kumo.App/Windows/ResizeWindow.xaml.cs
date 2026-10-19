using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kumo.Operations;

namespace Kumo.App.Windows;

/// <summary>
/// Resize by hand: the picture is shown over a dashed outline of its original size and the corner handle sets the
/// new size directly, with the exact pixel dimensions shown while dragging.
/// </summary>
public partial class ResizeWindow : Window
{
    private const double StageFill = 0.72;
    private const double MaxUpscale = 4;
    private const double MinimumDisplaySize = 8;

    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private readonly double _displayScale;
    private readonly Point _origin;
    private int _width;
    private int _height;
    private bool _updatingFields;

    private ResizeWindow(ImagePreview preview, string fileName, int fileCount)
    {
        InitializeComponent();
        _originalWidth = preview.Width;
        _originalHeight = preview.Height;
        _width = preview.Width;
        _height = preview.Height;
        Title = $"Resize — {fileName}";

        Picture.Source = Decode(preview.Png);
        _displayScale = Math.Min(MaxUpscale, Math.Min(Stage.Width * StageFill / _originalWidth, Stage.Height * StageFill / _originalHeight));
        double shownWidth = _originalWidth * _displayScale;
        double shownHeight = _originalHeight * _displayScale;
        // The original sits up and to the left of centre, leaving room to drag larger.
        _origin = new Point(Math.Max(16, (Stage.Width - shownWidth) / 2 * 0.5), Math.Max(16, (Stage.Height - shownHeight) / 2 * 0.5));

        OriginalOutline.Stroke = SystemColors.GrayTextBrush;
        OriginalOutline.Width = shownWidth;
        OriginalOutline.Height = shownHeight;
        Canvas.SetLeft(OriginalOutline, _origin.X);
        Canvas.SetTop(OriginalOutline, _origin.Y);
        Canvas.SetLeft(Picture, _origin.X);
        Canvas.SetTop(Picture, _origin.Y);

        Summary.Text = fileCount == 1
            ? "Drag the orange handle, or type a size."
            : $"Sized on {fileName}. The other {fileCount - 1} image{(fileCount == 2 ? "" : "s")} will be scaled by the same proportion.";

        WidthBox.TextChanged += (_, _) => OnFieldChanged(WidthBox, horizontal: true);
        HeightBox.TextChanged += (_, _) => OnFieldChanged(HeightBox, horizontal: false);
        KeepProportions.Checked += (_, _) => Apply(_width, (int)Math.Round(_width * (double)_originalHeight / _originalWidth), updateFields: true);
        Loaded += (_, _) =>
        {
            Activate();
            WidthBox.Focus();
            WidthBox.SelectAll();
        };
        Apply(_width, _height, updateFields: true);
    }

    public (double ScaleX, double ScaleY)? Result { get; private set; }

    /// <returns>The chosen scale factors, or null when cancelled.</returns>
    public static (double ScaleX, double ScaleY)? Ask(Window owner, ThemeMode theme, ImagePreview preview, string fileName, int fileCount)
    {
        var window = new ResizeWindow(preview, fileName, fileCount) { Owner = owner };
        if (!SystemParameters.HighContrast)
            window.ThemeMode = theme;
        return window.ShowDialog() == true ? window.Result : null;
    }

    private static BitmapSource Decode(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private int LargestWidth => (int)(_originalWidth * MaxUpscale);

    private int LargestHeight => (int)(_originalHeight * MaxUpscale);

    private void OnHandleDrag(object sender, DragDeltaEventArgs e)
    {
        double shownWidth = Math.Clamp(Picture.Width + e.HorizontalChange, MinimumDisplaySize, Stage.Width - _origin.X - 4);
        double shownHeight = Math.Clamp(Picture.Height + e.VerticalChange, MinimumDisplaySize, Stage.Height - _origin.Y - 4);
        if (KeepProportions.IsChecked == true)
        {
            // The axis the pointer moved most along drives the size; the handle snaps back to the corner on the
            // next update, so the other axis never drifts away from the pointer.
            double fullWidth = _originalWidth * _displayScale;
            double fullHeight = _originalHeight * _displayScale;
            double scale = Math.Abs(e.HorizontalChange) >= Math.Abs(e.VerticalChange) ? shownWidth / fullWidth : shownHeight / fullHeight;
            double roomScale = Math.Min((Stage.Width - _origin.X - 4) / fullWidth, (Stage.Height - _origin.Y - 4) / fullHeight);
            scale = Math.Min(scale, roomScale);
            Apply((int)Math.Round(_originalWidth * scale), (int)Math.Round(_originalHeight * scale), updateFields: true);
        }
        else
        {
            Apply((int)Math.Round(shownWidth / _displayScale), (int)Math.Round(shownHeight / _displayScale), updateFields: true);
        }
    }

    private void OnFieldChanged(TextBox box, bool horizontal)
    {
        if (_updatingFields || !int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value < 1)
            return;
        if (KeepProportions.IsChecked == true)
        {
            double scale = horizontal ? (double)value / _originalWidth : (double)value / _originalHeight;
            int other = (int)Math.Round((horizontal ? _originalHeight : _originalWidth) * scale);
            if (horizontal)
                Apply(value, other, updateFields: false, keepTyped: HeightBox);
            else
                Apply(other, value, updateFields: false, keepTyped: WidthBox);
        }
        else
        {
            Apply(horizontal ? value : _width, horizontal ? _height : value, updateFields: false);
        }
    }

    private void Apply(int width, int height, bool updateFields, TextBox? keepTyped = null)
    {
        _width = Math.Clamp(width, 1, LargestWidth);
        _height = Math.Clamp(height, 1, LargestHeight);

        Picture.Width = Math.Max(MinimumDisplaySize, _width * _displayScale);
        Picture.Height = Math.Max(MinimumDisplaySize, _height * _displayScale);
        Canvas.SetLeft(Handle, _origin.X + Picture.Width - Handle.Width / 2);
        Canvas.SetTop(Handle, _origin.Y + Picture.Height - Handle.Height / 2);

        double percent = 100.0 * _width / _originalWidth;
        SizeText.Text = $"{_width} × {_height} px · {percent:0}%";
        SizeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tagLeft = Math.Min(_origin.X + Picture.Width + 10, Stage.Width - SizeTag.DesiredSize.Width - 6);
        double tagTop = Math.Min(_origin.Y + Picture.Height + 10, Stage.Height - SizeTag.DesiredSize.Height - 6);
        Canvas.SetLeft(SizeTag, Math.Max(6, tagLeft));
        Canvas.SetTop(SizeTag, Math.Max(6, tagTop));

        _updatingFields = true;
        if (updateFields || keepTyped == WidthBox)
            WidthBox.Text = _width.ToString(CultureInfo.CurrentCulture);
        if (updateFields || keepTyped == HeightBox)
            HeightBox.Text = _height.ToString(CultureInfo.CurrentCulture);
        _updatingFields = false;

        SaveButton.IsEnabled = _width != _originalWidth || _height != _originalHeight;
    }

    private void OnOriginal(object sender, RoutedEventArgs e) => Apply(_originalWidth, _originalHeight, updateFields: true);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = ((double)_width / _originalWidth, (double)_height / _originalHeight);
        DialogResult = true;
    }
}
