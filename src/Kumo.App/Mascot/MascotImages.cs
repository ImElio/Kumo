using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kumo.App.Mascot;

public enum MascotState
{
    Idle,
    Dragging,
    Hovering,
    Converting,
    Compressing,
    Processing,
    Success,
    Error,
    Cancelled,
}

/// <summary>
/// Kumo's artwork, one square 256 px image per state, generated from assets/mascot by tools/Kumo.AssetGen and
/// embedded as resources. Images are decoded once and frozen so they can be shared freely.
/// </summary>
public static class MascotImages
{
    private static readonly Dictionary<MascotState, ImageSource> s_cache = [];

    public static ImageSource For(MascotState state)
    {
        lock (s_cache)
        {
            if (s_cache.TryGetValue(state, out var cached))
                return cached;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Kumo;component/Mascot/Images/{state.ToString().ToLowerInvariant()}.png");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            s_cache[state] = image;
            return image;
        }
    }
}
