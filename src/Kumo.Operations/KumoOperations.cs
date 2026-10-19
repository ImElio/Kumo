using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Images;
using Kumo.Operations.Pdf;

namespace Kumo.Operations;

public static class KumoOperations
{
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

        new ImageResizeOperation(ResizeMode.Percent, 75),
        new ImageResizeOperation(ResizeMode.Percent, 50),
        new ImageResizeOperation(ResizeMode.Percent, 25),
        new ImageResizeOperation(ResizeMode.LongEdge, 1920),
        new ImageResizeOperation(ResizeMode.LongEdge, 1280),

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
