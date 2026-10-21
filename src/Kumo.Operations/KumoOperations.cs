using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Images;
using Kumo.Operations.Pdf;

namespace Kumo.Operations;

public static class KumoOperations
{
    public const int MinimumPasswordLength = PdfProtectOperation.MinimumPasswordLength;

    /// <summary>Returns selected PDFs that require a user password to be imported into a combined document.</summary>
    public static IReadOnlyList<InputFile> FindPasswordProtectedPdfs(IReadOnlyList<InputFile> files) =>
        PdfMergeOperation.FindPasswordProtectedInputs(files);

    /// <summary>Every operation Kumo ships, in the order entries appear within a category.</summary>
    public static IReadOnlyList<IFileOperation> CreateAll() =>
    [
        new ImageConvertOperation(FileKind.Png),
        new ImageConvertOperation(FileKind.Jpeg),
        new ImageConvertOperation(FileKind.WebP),
        new ImageConvertOperation(FileKind.Bmp),
        new ImageToPdfOperation(),
        new PdfRenderOperation(FileKind.Png),
        new PdfRenderOperation(FileKind.Jpeg),

        new ImageCropOperation(),

        new ImageCompressOperation(CompressionLevel.High),
        new ImageCompressOperation(CompressionLevel.Balanced),
        new ImageCompressOperation(CompressionLevel.Smallest),
        new ImageCompressOperation(CompressionLevel.Lossless),

        new ImageRotateOperation(90),
        new ImageRotateOperation(270),
        new ImageRotateOperation(180),

        new PdfMergeOperation(),
        new ImagesToPdfOperation(),
        new PdfSplitOperation(),
        new PdfExtractPagesOperation(),
        new PdfProtectOperation(),
    ];
}
