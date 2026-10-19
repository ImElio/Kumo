using Kumo.Core.Files;
using Kumo.Core.Operations;
using SkiaSharp;

namespace Kumo.Operations.Images;

/// <summary>Decoding and encoding shared by the image operations.</summary>
internal static class ImageCodec
{
    public static bool CanDecode(FileKind kind) => kind is FileKind.Png or FileKind.Jpeg or FileKind.WebP or FileKind.Bmp or FileKind.Gif;

    public static bool CanEncode(FileKind kind) => kind is FileKind.Png or FileKind.Jpeg or FileKind.WebP or FileKind.Bmp;

    /// <summary>GIF cannot be written, so results derived from a GIF are saved as PNG.</summary>
    public static FileKind SameFormatTarget(FileKind source) => CanEncode(source) ? source : FileKind.Png;

    /// <summary>
    /// Decodes the first frame upright. The pixel count is checked from the header before any pixel memory is
    /// allocated, which protects against decompression bombs (tiny files declaring enormous dimensions).
    /// </summary>
    public static SKBitmap Decode(InputFile file, OperationLimits limits, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var codec = SKCodec.Create(stream, out var result)
            ?? throw OperationErrors.Corrupt($"{file.Name} couldn't be read as an image ({result}).");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            throw OperationErrors.Corrupt($"{file.Name} has no image data.");
        if ((long)info.Width * info.Height > limits.MaxImagePixels)
            throw OperationErrors.TooLarge($"{file.Name} is {info.Width}×{info.Height} pixels, more than Kumo can process safely.");

        var target = new SKImageInfo(info.Width, info.Height, SKImageInfo.PlatformColorType,
            info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
        var bitmap = new SKBitmap(target);
        var decodeResult = codec.GetPixels(target, bitmap.GetPixels());
        if (decodeResult is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw OperationErrors.Corrupt($"{file.Name} is damaged and couldn't be decoded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
            return bitmap;
        using (bitmap)
            return Transform(bitmap, OriginMatrix(codec.EncodedOrigin, bitmap.Width, bitmap.Height), SwapsAxes(codec.EncodedOrigin));
    }

    public static SKEncodedOrigin RotationOrigin(int clockwiseDegrees) => (((clockwiseDegrees % 360) + 360) % 360) switch
    {
        90 => SKEncodedOrigin.RightTop,
        180 => SKEncodedOrigin.BottomRight,
        270 => SKEncodedOrigin.LeftBottom,
        _ => SKEncodedOrigin.TopLeft,
    };

    public static SKBitmap Rotate(SKBitmap source, int clockwiseDegrees)
    {
        var origin = RotationOrigin(clockwiseDegrees);
        return Transform(source, OriginMatrix(origin, source.Width, source.Height), SwapsAxes(origin));
    }

    /// <summary>
    /// Downscales in successive halvings before a final cubic pass; a single bicubic step from a much larger
    /// image would alias visibly.
    /// </summary>
    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var current = source;
        bool ownsCurrent = false;
        try
        {
            while (current.Width / 2 >= width && current.Height / 2 >= height)
            {
                var half = current.Resize(new SKImageInfo(current.Width / 2, current.Height / 2, current.ColorType, current.AlphaType),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)) ?? throw OperationErrors.TooLarge("The image could not be resized.");
                if (ownsCurrent)
                    current.Dispose();
                current = half;
                ownsCurrent = true;
            }
            var result = current.Resize(new SKImageInfo(width, height, current.ColorType, current.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw OperationErrors.TooLarge("The image could not be resized.");
            return result;
        }
        finally
        {
            if (ownsCurrent)
                current.Dispose();
        }
    }

    public static SKBitmap Crop(SKBitmap source, int x, int y, int width, int height)
    {
        var result = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(x, y, x + width, y + height), new SKRect(0, 0, width, height));
        return result;
    }

    public static SKData Encode(SKBitmap bitmap, FileKind target, int quality)
    {
        switch (target)
        {
            case FileKind.Png:
                return EncodePng(bitmap, maximumCompression: false);
            case FileKind.Jpeg:
            {
                using var flat = FlattenOnWhite(bitmap);
                using var pixmap = flat.PeekPixels();
                return pixmap.Encode(new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
                    ?? throw new InvalidOperationException("JPEG encoding failed.");
            }
            case FileKind.WebP:
            {
                using var pixmap = bitmap.PeekPixels();
                return pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality))
                    ?? throw new InvalidOperationException("WEBP encoding failed.");
            }
            case FileKind.Bmp:
            {
                using var flat = FlattenOnWhite(bitmap);
                return SKData.CreateCopy(BmpWriter.Write(flat));
            }
            default:
                throw OperationErrors.Unsupported($"Saving as {target.DisplayName()} isn't supported.");
        }
    }

    public static SKData EncodePng(SKBitmap bitmap, bool maximumCompression)
    {
        using var pixmap = bitmap.PeekPixels();
        var options = maximumCompression
            ? new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9)
            : new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6);
        return pixmap.Encode(options) ?? throw new InvalidOperationException("PNG encoding failed.");
    }

    /// <summary>Formats without transparency get a white background, matching what most viewers show.</summary>
    public static SKBitmap FlattenOnWhite(SKBitmap source)
    {
        var flat = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKImageInfo.PlatformColorType, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(flat);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, 0, 0);
        return flat;
    }

    public static async Task<string> WriteAsync(IOutputWriter outputs, string directory, string fileName, SKData data, CancellationToken cancellationToken) =>
        await outputs.WriteAsync(directory, fileName, data.Size, async (stream, ct) =>
        {
            using var source = data.AsStream(streamDisposesData: false);
            await source.CopyToAsync(stream, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    // Maps source pixels (W×H) to their upright position for each EXIF orientation.
    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static SKBitmap Transform(SKBitmap source, SKMatrix matrix, bool swap)
    {
        int width = swap ? source.Height : source.Width;
        int height = swap ? source.Width : source.Height;
        var result = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
