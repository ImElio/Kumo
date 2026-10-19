using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Images;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace Kumo.Operations.Pdf;

/// <summary>
/// Places each image on an A4 page oriented like the image. Images are shown at 96 DPI and only scaled down when
/// they do not fit, so small graphics are not blown up.
/// </summary>
internal static class ImagePages
{
    private const double PointsPerPixel = 72.0 / 96.0;

    public static bool Supports(InputFile file) => ImageCodec.CanDecode(file.Kind);

    public static void AddPage(PdfDocument document, InputFile file, OperationLimits limits, CancellationToken cancellationToken)
    {
        using var bitmap = ImageCodec.Decode(file, limits, cancellationToken);
        using var encoded = bitmap.AlphaType == SKAlphaType.Opaque && file.Kind == FileKind.Jpeg
            ? ImageCodec.Encode(bitmap, FileKind.Jpeg, 92)
            : ImageCodec.EncodePng(bitmap, maximumCompression: false);
        using var stream = encoded.AsStream(streamDisposesData: false);
        using var image = XImage.FromStream(stream);

        var page = document.AddPage();
        page.Size = PageSize.A4;
        page.Orientation = bitmap.Width > bitmap.Height ? PageOrientation.Landscape : PageOrientation.Portrait;
        double pageWidth = page.Width.Point;
        double pageHeight = page.Height.Point;
        double width = bitmap.Width * PointsPerPixel;
        double height = bitmap.Height * PointsPerPixel;
        double scale = Math.Min(1, Math.Min(pageWidth / width, pageHeight / height));
        width *= scale;
        height *= scale;

        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(image, (pageWidth - width) / 2, (pageHeight - height) / 2, width, height);
    }
}

internal sealed class ImageToPdfOperation : IFileOperation
{
    public string Id => "image.convert.pdf";

    public OperationCategory Category => OperationCategory.Convert;

    public string Label => "PDF";

    public string Description => "Convert to PDF";

    public bool IsCombined => false;

    public InputRequirement Requirement => InputRequirement.None;

    public bool Supports(InputFile file) => ImagePages.Supports(file);

    public async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var input = inputs[0];
        using var document = new PdfDocument();
        ImagePages.AddPage(document, input, context.Limits, cancellationToken);
        string path = await PdfFiles.SaveAsync(document, context.Outputs, context.Outputs.DirectoryFor(input), $"{input.Stem}.pdf",
            input.Length * 2, cancellationToken).ConfigureAwait(false);
        return OperationResult.Of(path);
    }
}

internal sealed class ImagesToPdfOperation : IFileOperation
{
    public string Id => "image.merge.pdf";

    public OperationCategory Category => OperationCategory.Merge;

    public string Label => "Combine into PDF";

    public string Description => "Combine images into one PDF (sorted by name)";

    public bool IsCombined => true;

    public int MinimumFiles => 2;

    public InputRequirement Requirement => InputRequirement.None;

    public bool Supports(InputFile file) => ImagePages.Supports(file);

    public async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var ordered = PdfFiles.NaturalOrder(inputs);
        using var document = new PdfDocument();
        for (int i = 0; i < ordered.Count; i++)
        {
            ImagePages.AddPage(document, ordered[i], context.Limits, cancellationToken);
            context.Progress?.Report((i + 1.0) / (ordered.Count + 1));
        }
        var first = ordered[0];
        string path = await PdfFiles.SaveAsync(document, context.Outputs, context.Outputs.DirectoryFor(first), $"{first.Stem} (combined).pdf",
            inputs.Sum(f => f.Length) * 2, cancellationToken).ConfigureAwait(false);
        return OperationResult.Of(path);
    }
}
