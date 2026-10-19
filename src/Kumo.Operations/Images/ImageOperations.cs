using System.Globalization;
using Kumo.Core.Files;
using Kumo.Core.Operations;
using SkiaSharp;

namespace Kumo.Operations.Images;

internal abstract class PerFileImageOperation : IFileOperation
{
    public abstract string Id { get; }

    public abstract OperationCategory Category { get; }

    public abstract string Label { get; }

    public abstract string Description { get; }

    public bool IsCombined => false;

    public virtual InputRequirement Requirement => InputRequirement.None;

    public virtual bool Supports(InputFile file) => ImageCodec.CanDecode(file.Kind);

    public async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        string? note = null;
        foreach (var input in inputs)
        {
            var result = await ProcessAsync(input, context, cancellationToken).ConfigureAwait(false);
            outputs.AddRange(result.Outputs);
            note ??= result.Note;
            context.Progress?.Report(1);
        }
        return new OperationResult(outputs, note);
    }

    protected abstract Task<OperationResult> ProcessAsync(InputFile input, OperationContext context, CancellationToken cancellationToken);

    protected static string Named(InputFile input, string suffix, FileKind kind) =>
        $"{input.Stem}{(suffix.Length > 0 ? " " + suffix : string.Empty)}{kind.Extension()}";
}

internal sealed class ImageConvertOperation(FileKind target) : PerFileImageOperation
{
    public const int JpegQuality = 90;
    public const int WebpQuality = 85;

    public override string Id => $"image.convert.{target.ToString().ToLowerInvariant()}";

    public override OperationCategory Category => OperationCategory.Convert;

    public override string Label => target.DisplayName();

    public override string Description => $"Convert to {target.DisplayName()}";

    public override bool Supports(InputFile file) => base.Supports(file) && file.Kind != target;

    protected override async Task<OperationResult> ProcessAsync(InputFile input, OperationContext context, CancellationToken cancellationToken)
    {
        using var data = EncodeConverted(input, context, cancellationToken);
        string path = await ImageCodec.WriteAsync(context.Outputs, context.Outputs.DirectoryFor(input), Named(input, string.Empty, target), data, cancellationToken).ConfigureAwait(false);
        return OperationResult.Of(path);
    }

    private SKData EncodeConverted(InputFile input, OperationContext context, CancellationToken cancellationToken)
    {
        using var bitmap = ImageCodec.Decode(input, context.Limits, cancellationToken);
        return ImageCodec.Encode(bitmap, target, target == FileKind.WebP ? WebpQuality : JpegQuality);
    }
}

/// <summary>
/// Resizes to the proportion chosen in the resize editor. The editor works on the first image; every image in the
/// selection is scaled by the same factors, so a batch keeps its relative sizes.
/// </summary>
internal sealed class ImageResizeOperation : PerFileImageOperation
{
    public const double MaxUpscale = 4;

    public override string Id => "image.resize";

    public override OperationCategory Category => OperationCategory.Resize;

    public override string Label => "Resize";

    public override string Description => "Resize…";

    public override InputRequirement Requirement => InputRequirement.Dimensions;

    public static (int Width, int Height)? TargetSize(int width, int height, double scaleX, double scaleY)
    {
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            throw OperationErrors.InvalidInput("Choose a size larger than zero.");
        if (scaleX > MaxUpscale || scaleY > MaxUpscale)
            throw OperationErrors.InvalidInput($"Kumo enlarges images at most {MaxUpscale:0}×.");
        int targetWidth = Math.Max(1, (int)Math.Round(width * scaleX));
        int targetHeight = Math.Max(1, (int)Math.Round(height * scaleY));
        return targetWidth == width && targetHeight == height ? null : (targetWidth, targetHeight);
    }

    protected override async Task<OperationResult> ProcessAsync(InputFile input, OperationContext context, CancellationToken cancellationToken)
    {
        double scaleX = context.Options.ScaleX ?? throw OperationErrors.InvalidInput("No size was chosen.");
        double scaleY = context.Options.ScaleY ?? scaleX;
        var kind = ImageCodec.SameFormatTarget(input.Kind);
        SKData data;
        string suffix;
        using (var bitmap = ImageCodec.Decode(input, context.Limits, cancellationToken))
        {
            if (TargetSize(bitmap.Width, bitmap.Height, scaleX, scaleY) is not { } size)
                return new OperationResult([], $"{input.Name} already has that size.");
            if ((long)size.Width * size.Height > context.Limits.MaxImagePixels)
                throw OperationErrors.TooLarge("The enlarged image would be too large.");
            using var resized = ImageCodec.Resize(bitmap, size.Width, size.Height);
            data = ImageCodec.Encode(resized, kind, kind == FileKind.WebP ? ImageConvertOperation.WebpQuality : ImageConvertOperation.JpegQuality);
            suffix = $"({size.Width}×{size.Height})";
        }
        using (data)
        {
            string path = await ImageCodec.WriteAsync(context.Outputs, context.Outputs.DirectoryFor(input), Named(input, suffix, kind), data, cancellationToken).ConfigureAwait(false);
            return OperationResult.Of(path);
        }
    }
}

internal enum CompressionLevel
{
    High,
    Balanced,
    Smallest,
    Lossless,
}

/// <summary>
/// Re-encodes JPEG and WEBP at a lower quality, or PNG with maximum lossless compression. A result that is not
/// smaller than the original is discarded rather than saved.
/// </summary>
internal sealed class ImageCompressOperation(CompressionLevel level) : PerFileImageOperation
{
    public override string Id => $"image.compress.{level.ToString().ToLowerInvariant()}";

    public override OperationCategory Category => OperationCategory.Compress;

    public override string Label => level switch
    {
        CompressionLevel.High => "High quality",
        CompressionLevel.Smallest => "Smallest",
        CompressionLevel.Lossless => "Lossless",
        _ => "Balanced",
    };

    public override string Description => level == CompressionLevel.Lossless ? "Optimize PNG without quality loss" : $"Compress ({Label.ToLowerInvariant()})";

    public int Quality => level switch
    {
        CompressionLevel.High => 85,
        CompressionLevel.Smallest => 55,
        _ => 72,
    };

    public override bool Supports(InputFile file) => level == CompressionLevel.Lossless
        ? file.Kind == FileKind.Png
        : file.Kind is FileKind.Jpeg or FileKind.WebP;

    protected override async Task<OperationResult> ProcessAsync(InputFile input, OperationContext context, CancellationToken cancellationToken)
    {
        SKData data;
        using (var bitmap = ImageCodec.Decode(input, context.Limits, cancellationToken))
            data = level == CompressionLevel.Lossless ? ImageCodec.EncodePng(bitmap, maximumCompression: true) : ImageCodec.Encode(bitmap, input.Kind, Quality);

        using (data)
        {
            if (data.Size >= input.Length)
                return new OperationResult([], $"{input.Name} is already compressed as much as this setting allows.");
            string suffix = level == CompressionLevel.Lossless ? "(optimized)" : "(compressed)";
            string path = await ImageCodec.WriteAsync(context.Outputs, context.Outputs.DirectoryFor(input), Named(input, suffix, input.Kind), data, cancellationToken).ConfigureAwait(false);
            return OperationResult.Of(path);
        }
    }
}

internal sealed class ImageRotateOperation(int clockwiseDegrees) : PerFileImageOperation
{
    private const int RotatedJpegQuality = 92;

    public override string Id => $"image.rotate.{clockwiseDegrees}";

    public override OperationCategory Category => OperationCategory.Rotate;

    public override string Label => clockwiseDegrees switch
    {
        90 => "90° right",
        270 => "90° left",
        _ => clockwiseDegrees.ToString(CultureInfo.InvariantCulture) + "°",
    };

    public override string Description => clockwiseDegrees switch
    {
        90 => "Rotate 90° clockwise",
        270 => "Rotate 90° counterclockwise",
        _ => $"Rotate {clockwiseDegrees}°",
    };

    protected override async Task<OperationResult> ProcessAsync(InputFile input, OperationContext context, CancellationToken cancellationToken)
    {
        var kind = ImageCodec.SameFormatTarget(input.Kind);
        SKData data;
        using (var bitmap = ImageCodec.Decode(input, context.Limits, cancellationToken))
        using (var rotated = ImageCodec.Rotate(bitmap, clockwiseDegrees))
            data = ImageCodec.Encode(rotated, kind, kind == FileKind.WebP ? ImageConvertOperation.WebpQuality : RotatedJpegQuality);

        using (data)
        {
            string path = await ImageCodec.WriteAsync(context.Outputs, context.Outputs.DirectoryFor(input), Named(input, "(rotated)", kind), data, cancellationToken).ConfigureAwait(false);
            return OperationResult.Of(path);
        }
    }
}
