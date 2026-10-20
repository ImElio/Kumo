using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kumo.Core.Updates;

namespace Kumo.App.Updates;

/// <summary>Turns parsed release notes into native text elements that follow the window's Fluent theme.</summary>
internal static class ReleaseNotesView
{
    private const long MaxImageBytes = 8 * 1024 * 1024;
    private const int MaxRedirects = 3;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Kumo", UpdateCoordinator.ProductVersion().ToShortString()));
        return client;
    });

    public static FrameworkElement Build(string? markdown)
    {
        var panel = new StackPanel();
        var blocks = ReleaseNotes.Parse(markdown);
        if (blocks.Count == 0)
        {
            panel.Children.Add(Secondary(new TextBlock { Text = "No release notes were published for this version.", TextWrapping = TextWrapping.Wrap }));
            return panel;
        }

        foreach (var block in blocks)
        {
            if (block is { Kind: NotesBlockKind.Image, Image: { } image })
            {
                panel.Children.Add(Screenshot(image, block.Inlines.FirstOrDefault()?.Text));
                continue;
            }
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
            AddInlines(text.Inlines, block.Inlines);
            switch (block.Kind)
            {
                case NotesBlockKind.Heading:
                    text.FontSize = block.Level == 1 ? 18 : block.Level == 2 ? 15 : 14;
                    text.FontWeight = FontWeights.SemiBold;
                    text.Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 6);
                    panel.Children.Add(text);
                    break;
                case NotesBlockKind.Bullet or NotesBlockKind.Numbered:
                    string marker = block.Kind == NotesBlockKind.Bullet ? "•" : $"{block.Level}.";
                    var row = new DockPanel { Margin = new Thickness(4, 2, 0, 2) };
                    var bullet = new TextBlock { Text = marker, Width = 18, FontSize = 13, LineHeight = 20 };
                    DockPanel.SetDock(bullet, Dock.Left);
                    row.Children.Add(Secondary(bullet));
                    row.Children.Add(text);
                    panel.Children.Add(row);
                    break;
                default:
                    text.Margin = new Thickness(0, 4, 0, 4);
                    panel.Children.Add(text);
                    break;
            }
        }
        return panel;
    }

    private static FrameworkElement Screenshot(Uri url, string? alt)
    {
        var status = Secondary(new TextBlock { Text = "Loading screenshot…", FontSize = 12, Margin = new Thickness(16, 24, 16, 24), HorizontalAlignment = HorizontalAlignment.Center });
        var frame = new Border
        {
            Margin = new Thickness(0, 8, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = status,
            ToolTip = alt is { Length: > 0 } ? alt : null,
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        if (alt is { Length: > 0 })
            System.Windows.Automation.AutomationProperties.SetName(frame, alt);
        _ = LoadAsync(frame, status, url, alt);
        return frame;
    }

    private static async Task LoadAsync(Border frame, TextBlock status, Uri url, string? alt)
    {
        BitmapSource? bitmap = null;
        try
        {
            byte[]? bytes = await DownloadImageAsync(url).ConfigureAwait(true);
            if (bytes != null)
                bitmap = Decode(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            // A screenshot that cannot be shown must not affect the rest of the notes.
        }

        if (bitmap == null)
        {
            status.Text = alt is { Length: > 0 } ? $"Screenshot unavailable: {alt}" : "Screenshot unavailable";
            return;
        }
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = bitmap.PixelWidth, Cursor = System.Windows.Input.Cursors.Hand };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.MouseLeftButtonUp += (_, _) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        frame.Child = image;
    }

    /// <summary>
    /// Downloads an image allowed by <see cref="ReleaseNotes.IsAllowedImage"/>. Redirects are followed by hand so every
    /// hop stays on a GitHub image host, and the body is refused past <see cref="MaxImageBytes"/>.
    /// </summary>
    private static async Task<byte[]?> DownloadImageAsync(Uri url)
    {
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!ReleaseNotes.IsAllowedImage(url))
                return null;
            using var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } next)
                    return null;
                url = next.IsAbsoluteUri ? next : new Uri(url, next);
                continue;
            }
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType is not { } type || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentLength > MaxImageBytes)
                return null;

            await using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                    return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        return null;
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        int width = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0].PixelWidth;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = Math.Min(width, 1200);
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static void AddInlines(InlineCollection target, IReadOnlyList<NotesInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline.Kind)
            {
                case NotesInlineKind.Bold:
                    target.Add(new Bold(new Run(inline.Text)));
                    break;
                case NotesInlineKind.Code:
                    var code = new Run(inline.Text) { FontFamily = Mono, FontSize = 12 };
                    code.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                    target.Add(code);
                    break;
                case NotesInlineKind.Link when inline.Url is { } url:
                    var link = new Hyperlink(new Run(inline.Text)) { NavigateUri = url, ToolTip = url.AbsoluteUri };
                    link.RequestNavigate += (_, e) =>
                    {
                        if (e.Uri.Scheme == Uri.UriSchemeHttps)
                            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        e.Handled = true;
                    };
                    target.Add(link);
                    break;
                default:
                    target.Add(new Run(inline.Text));
                    break;
            }
        }
    }

    private static TextBlock Secondary(TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return text;
    }
}
