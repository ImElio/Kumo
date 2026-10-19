using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Images;

namespace Kumo.Operations;

/// <param name="Png">A downscaled PNG for display.</param>
/// <param name="Width">Width of the full image, after EXIF orientation.</param>
/// <param name="Height">Height of the full image, after EXIF orientation.</param>
public sealed record ImagePreview(byte[] Png, int Width, int Height)
{
    /// <summary>
    /// Decodes with the same safety limits and orientation handling as the operations, so the editor shows exactly
    /// what will be resized. WPF's own decoders cannot read WEBP without an optional Windows extension.
    /// </summary>
    public static ImagePreview Load(InputFile file, int maxEdge, CancellationToken cancellationToken = default)
    {
        using var bitmap = ImageCodec.Decode(file, OperationLimits.Default, cancellationToken);
        double scale = Math.Min(1, (double)maxEdge / Math.Max(bitmap.Width, bitmap.Height));
        if (scale >= 1)
        {
            using var full = ImageCodec.EncodePng(bitmap, maximumCompression: false);
            return new ImagePreview(full.ToArray(), bitmap.Width, bitmap.Height);
        }
        using var small = ImageCodec.Resize(bitmap, (int)Math.Round(bitmap.Width * scale), (int)Math.Round(bitmap.Height * scale));
        using var data = ImageCodec.EncodePng(small, maximumCompression: false);
        return new ImagePreview(data.ToArray(), bitmap.Width, bitmap.Height);
    }
}
