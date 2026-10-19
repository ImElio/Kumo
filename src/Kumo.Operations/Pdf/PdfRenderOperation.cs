using System.Runtime.InteropServices;
using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Images;
using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace Kumo.Operations.Pdf;

/// <summary>
/// Renders PDF pages with the PDF engine built into Windows (Windows.Data.Pdf), so no third-party renderer is
/// bundled. A single page is saved next to the PDF; several pages go into a new folder.
/// </summary>
internal sealed class PdfRenderOperation(FileKind target) : IFileOperation
{
    public const double RenderDpi = 150;

    public string Id => $"pdf.render.{target.ToString().ToLowerInvariant()}";

    public OperationCategory Category => OperationCategory.Convert;

    public string Label => target.DisplayName();

    public string Description => $"Save pages as {target.DisplayName()} images";

    public bool IsCombined => false;

    public InputRequirement Requirement => InputRequirement.None;

    public bool Supports(InputFile file) => file.Kind == FileKind.Pdf;

    public async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var input = inputs[0];
        var document = await Load(input).ConfigureAwait(false);
        int pageCount = (int)document.PageCount;
        if (pageCount == 0)
            throw OperationErrors.Corrupt($"{input.Name} has no pages.");
        if (pageCount > context.Limits.MaxRenderedPages)
            throw OperationErrors.TooLarge($"{input.Name} has {pageCount} pages; Kumo saves at most {context.Limits.MaxRenderedPages} pages as images.");

        string directory = context.Outputs.DirectoryFor(input);
        string folder = pageCount == 1 ? directory : context.Outputs.CreateFolder(directory, $"{input.Stem} (images)");
        var outputs = new List<string>();
        try
        {
            for (uint index = 0; index < pageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] png = await RenderPage(document, index, context.Limits.MaxRenderedEdge).ConfigureAwait(false);
                using var data = target == FileKind.Png ? SKData.CreateCopy(png) : ToJpeg(png);
                string name = pageCount == 1
                    ? $"{input.Stem}{target.Extension()}"
                    : $"{input.Stem} - page {PdfFiles.PageNumber((int)index + 1, pageCount)}{target.Extension()}";
                outputs.Add(await ImageCodec.WriteAsync(context.Outputs, folder, name, data, cancellationToken).ConfigureAwait(false));
                context.Progress?.Report((index + 1.0) / pageCount);
            }
        }
        catch when (pageCount > 1)
        {
            PdfSplitOperation.TryDeleteFolder(folder);
            throw;
        }
        return new OperationResult(outputs);
    }

    private static async Task<WinPdf.PdfDocument> Load(InputFile input)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(input.Path);
            return await WinPdf.PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            // Windows reports a missing password as ERROR_WRONG_PASSWORD (0x8007052B).
            if (ex.HResult == unchecked((int)0x8007052B))
                throw new OperationFailedException(OperationErrorKind.Encrypted, $"{input.Name} is password-protected.", ex);
            throw OperationErrors.Corrupt($"{input.Name} couldn't be opened. It may be damaged.", ex);
        }
    }

    private static async Task<byte[]> RenderPage(WinPdf.PdfDocument document, uint index, int maxEdge)
    {
        using var page = document.GetPage(index);
        double scale = RenderDpi / 96.0;
        double width = Math.Max(1, page.Size.Width * scale);
        double height = Math.Max(1, page.Size.Height * scale);
        double fit = Math.Min(1, maxEdge / Math.Max(width, height));

        using var memory = new InMemoryRandomAccessStream();
        var options = new WinPdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Round(width * fit),
            DestinationHeight = (uint)Math.Round(height * fit),
            BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
        };
        await page.RenderToStreamAsync(memory, options);

        using var stream = memory.AsStreamForRead();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static SKData ToJpeg(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png) ?? throw OperationErrors.Corrupt("A page could not be rendered.");
        return ImageCodec.Encode(bitmap, FileKind.Jpeg, ImageConvertOperation.JpegQuality);
    }
}
