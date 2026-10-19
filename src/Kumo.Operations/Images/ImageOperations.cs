using System.Globalization;
using Kumo.Core.Files;
using Kumo.Core.Imaging;
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
/// Cuts out the region chosen in the crop editor at full resolution. The region is stored as fractions of the first
/// image, so every image in a selection is cropped the same way.
/// </summary>
internal sealed class ImageCropOperation : PerFileImageOperation
{
    public override string Id => "image.crop";

    public override OperationCategory Category => OperationCategory.Crop;

    public override string Label => "Crop";

    public override string Description => "Crop…";

    public override InputRequirement Requirement => InputRequirement.CropRegion;

    protected override async Task<OperationResult> ProcessAsync(InputFile input, OperationContext context, CancellationToken cancellationToken)
    {
        var region = context.Options.Crop ?? throw OperationErrors.InvalidInput("No crop area was chosen.");
        if (!(region.Width > 0 && region.Height > 0 && region.X >= 0 && region.Y >= 0 && region.Right <= 1.0001 && region.Bottom <= 1.0001))
            throw OperationErrors.InvalidInput("The crop area lies outside the image.");

        var kind = ImageCodec.SameFormatTarget(input.Kind);
        SKData data;
        string suffix;
        using (var bitmap = ImageCodec.Decode(input, context.Limits, cancellationToken))
        {
            var (x, y, width, height) = CropRect.ToPixels(region, bitmap.Width, bitmap.Height);
            if (width == bitmap.Width && height == bitmap.Height)
                return new OperationResult([], $"The crop area covers all of {input.Name}.");
            using var cropped = ImageCodec.Crop(bitmap, x, y, width, height);
            data = ImageCodec.Encode(cropped, kind, kind == FileKind.WebP ? ImageConvertOperation.WebpQuality : ImageConvertOperation.JpegQuality);
            suffix = $"(cropped {width}×{height})";
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
