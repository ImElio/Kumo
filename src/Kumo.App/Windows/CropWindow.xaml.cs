using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Kumo.Core.Imaging;
using Kumo.Operations;

namespace Kumo.App.Windows;

/// <summary>
/// Crop by hand: a frame over the picture with corner and edge handles, the area outside dimmed, a rule-of-thirds
/// grid and the exact pixel size of the result.
/// </summary>
public partial class CropWindow : Window
{
    private const double MaxStageWidth = 760;
    private const double MaxStageHeight = 500;

    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private readonly CropCanvas _canvas;

    private CropWindow(ImagePreview preview, string fileName, int fileCount)
    {
        InitializeComponent();
        _imageWidth = preview.Width;
        _imageHeight = preview.Height;
        Title = $"Crop — {fileName}";

        double scale = Math.Min(MaxStageWidth / _imageWidth, MaxStageHeight / _imageHeight);
        Picture.Source = Decode(preview.Png);
        Picture.Width = _imageWidth * scale;
        Picture.Height = _imageHeight * scale;
        _canvas = new CropCanvas(_imageWidth, _imageHeight, scale);
        Stage.Children.Add(_canvas);
        _canvas.CropChanged += UpdateSize;

        AddRatio("Free", null, isDefault: true);
        AddRatio("Original", (double)_imageWidth / _imageHeight);
        AddRatio("1:1", 1);
        AddRatio(_imageWidth >= _imageHeight ? "4:3" : "3:4", _imageWidth >= _imageHeight ? 4.0 / 3 : 3.0 / 4);
        AddRatio(_imageWidth >= _imageHeight ? "3:2" : "2:3", _imageWidth >= _imageHeight ? 3.0 / 2 : 2.0 / 3);
        AddRatio(_imageWidth >= _imageHeight ? "16:9" : "9:16", _imageWidth >= _imageHeight ? 16.0 / 9 : 9.0 / 16);

        Hint.Text = fileCount == 1
            ? "Drag the corners or edges, drag inside to move, or draw a new frame outside it."
            : $"Cropped on {fileName}; the other {fileCount - 1} image{(fileCount == 2 ? "" : "s")} get the same area, relative to their size.";
        Loaded += (_, _) =>
        {
            Activate();
            _canvas.Focus();
        };
        _canvas.Crop = CropMath.Initial(_imageWidth, _imageHeight, null);
    }

    public CropRect? Result { get; private set; }

    /// <returns>The crop area as fractions of the image, or null when cancelled.</returns>
    public static CropRect? Ask(Window owner, ThemeMode theme, ImagePreview preview, string fileName, int fileCount)
    {
        var window = new CropWindow(preview, fileName, fileCount) { Owner = owner };
        if (!SystemParameters.HighContrast)
            window.ThemeMode = theme;
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void AddRatio(string label, double? ratio, bool isDefault = false)
    {
        var button = new RadioButton
        {
            Content = label,
            GroupName = "Ratio",
            IsChecked = isDefault,
            Margin = new Thickness(0, 0, 14, 0),
            MinWidth = 0,
        };
        button.Checked += (_, _) =>
        {
            _canvas.Ratio = ratio;
            _canvas.Crop = CropMath.ApplyRatio(_canvas.Crop, _imageWidth, _imageHeight, ratio);
        };
        Ratios.Children.Add(button);
    }

    private void UpdateSize()
    {
        var (_, _, width, height) = CropRect.ToPixels(_canvas.Crop.Normalize(_imageWidth, _imageHeight), _imageWidth, _imageHeight);
        SizeText.Text = $"{width} × {height} px";
        SaveButton.IsEnabled = width != _imageWidth || height != _imageHeight;
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

    private void OnReset(object sender, RoutedEventArgs e) =>
        _canvas.Crop = CropMath.Initial(_imageWidth, _imageHeight, _canvas.Ratio);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = _canvas.Crop.Normalize(_imageWidth, _imageHeight);
        DialogResult = true;
    }
}
