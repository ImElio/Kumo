using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace Kumo.Operations.Tests;

public sealed class PdfOperationTests : IDisposable
{
    private readonly Fixtures _fixtures = new();

    public void Dispose() => _fixtures.Dispose();

    private static int PageCount(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Fact]
    public async Task Merge_combines_all_pages_in_natural_name_order()
    {
        var second = _fixtures.Pdf("part 10.pdf", 3);
        var first = _fixtures.Pdf("part 2.pdf", 2);

        var result = await Fixtures.Run(new PdfMergeOperation(), [second, first]);

        string output = result.Outputs.Single();
        Assert.Equal("part 2 (merged).pdf", Path.GetFileName(output));
        Assert.Equal(5, PageCount(output));
    }

    [Fact]
    public async Task Split_writes_one_file_per_page_into_a_new_folder()
    {
        var source = _fixtures.Pdf("report.pdf", 3);

        var result = await Fixtures.Run(new PdfSplitOperation(), [source]);

        Assert.Equal(3, result.Outputs.Count());
        Assert.All(result.Outputs, o => Assert.Equal(1, PageCount(o)));
        Assert.Equal("report (pages)", Path.GetFileName(Path.GetDirectoryName(result.Outputs.First())));
        Assert.Equal("report - page 001.pdf", Path.GetFileName(result.Outputs.First()));
    }

    [Fact]
    public async Task Split_of_a_single_page_document_saves_nothing()
    {
        var source = _fixtures.Pdf("one.pdf", 1);

        var result = await Fixtures.Run(new PdfSplitOperation(), [source]);

        Assert.Empty(result.Outputs);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(Directory.GetDirectories(_fixtures.Directory));
    }

    [Fact]
    public async Task Extract_keeps_only_the_requested_pages()
    {
        var source = _fixtures.Pdf("book.pdf", 6);

        var result = await Fixtures.Run(new PdfExtractPagesOperation(), [source], new OperationOptions(PageRange: "2-3, 6"));

        string output = result.Outputs.Single();
        Assert.Equal(3, PageCount(output));
        Assert.Equal("book (pages 2-3,6).pdf", Path.GetFileName(output));
    }

    [Fact]
    public async Task Extract_with_an_invalid_range_explains_why()
    {
        var source = _fixtures.Pdf("short.pdf", 2);

        var result = await Fixtures.Run(new PdfExtractPagesOperation(), [source], new OperationOptions(PageRange: "5"));

        Assert.Equal(OperationErrorKind.InvalidInput, result.FirstFailure!.Error);
        Assert.Contains("only 2 pages", result.FirstFailure.Message);
    }

    [Fact]
    public async Task Protect_requires_the_password_to_open_the_result()
    {
        var source = _fixtures.Pdf("secret.pdf", 2);

        var result = await Fixtures.Run(new PdfProtectOperation(), [source], new OperationOptions(Password: "kumo-1234"));

        string output = result.Outputs.Single();
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(output, PdfDocumentOpenMode.Import));
        using var opened = PdfReader.Open(output, "kumo-1234", PdfDocumentOpenMode.Import);
        Assert.Equal(2, opened.PageCount);
    }

    [Fact]
    public async Task Protect_rejects_short_passwords()
    {
        var source = _fixtures.Pdf("weak.pdf", 1);

        var result = await Fixtures.Run(new PdfProtectOperation(), [source], new OperationOptions(Password: "abc"));

        Assert.Equal(OperationErrorKind.InvalidInput, result.FirstFailure!.Error);
    }

    [Fact]
    public async Task Encrypted_inputs_are_reported_as_protected()
    {
        var locked = _fixtures.Pdf("locked.pdf", 1, password: "secret");
        var plain = _fixtures.Pdf("plain.pdf", 1);

        var result = await Fixtures.Run(new PdfMergeOperation(), [locked, plain]);

        Assert.Equal(OperationErrorKind.Encrypted, result.FirstFailure!.Error);
        Assert.Contains("password", result.FirstFailure.Message);
    }

    [Fact]
    public async Task Corrupt_pdfs_fail_cleanly()
    {
        var broken = _fixtures.Garbage("broken.pdf", "%PDF-1.7\n"u8);

        var result = await Fixtures.Run(new PdfSplitOperation(), [broken]);

        Assert.Equal(OperationErrorKind.Corrupt, result.FirstFailure!.Error);
        Assert.Empty(Directory.GetDirectories(_fixtures.Directory));
    }

    [Fact]
    public async Task Pages_render_to_png_with_the_windows_pdf_engine()
    {
        var source = _fixtures.Pdf("slides.pdf", 2);

        var result = await Fixtures.Run(new PdfRenderOperation(FileKind.Png), [source]);

        Assert.Equal(2, result.Outputs.Count());
        Assert.Equal("slides (images)", Path.GetFileName(Path.GetDirectoryName(result.Outputs.First())));
        using var page = SKBitmap.Decode(result.Outputs.First());
        // PDFsharp uses the system's default paper size for this fixture: A4 on some machines, Letter on others.
        bool isA4 = page.Width is >= 1230 and <= 1250 && page.Height is >= 1745 and <= 1760;
        bool isLetter = page.Width is >= 1260 and <= 1285 && page.Height is >= 1635 and <= 1665;
        Assert.True(isA4 || isLetter, $"Expected an A4 or Letter page at 150 DPI, got {page.Width}×{page.Height}.");
        var blue = page.GetPixel((int)(140 * 150 / 72.0), (int)(90 * 150 / 72.0));
        Assert.True(blue.Blue > blue.Red, $"Expected the drawn rectangle, got {blue}.");
    }

    [Fact]
    public async Task Single_page_renders_to_jpeg_beside_the_pdf()
    {
        var source = _fixtures.Pdf("flyer.pdf", 1);

        var result = await Fixtures.Run(new PdfRenderOperation(FileKind.Jpeg), [source]);

        string output = result.Outputs.Single();
        Assert.Equal(Path.Combine(_fixtures.Directory, "flyer.jpg"), output);
        Assert.Equal(FileKind.Jpeg, _fixtures.Resolve(output).Kind);
    }

    [Fact]
    public async Task Rendering_a_protected_pdf_is_reported_as_protected()
    {
        var locked = _fixtures.Pdf("render-locked.pdf", 1, password: "secret");

        var result = await Fixtures.Run(new PdfRenderOperation(FileKind.Png), [locked]);

        Assert.Equal(OperationErrorKind.Encrypted, result.FirstFailure!.Error);
    }

    [Fact]
    public async Task Image_becomes_a_one_page_pdf()
    {
        var image = _fixtures.Image("scan.jpg", SKEncodedImageFormat.Jpeg, 1200, 800);

        var result = await Fixtures.Run(new ImageToPdfOperation(), [image]);

        Assert.Equal(1, PageCount(result.Outputs.Single()));
        Assert.Equal(FileKind.Pdf, _fixtures.Resolve(result.Outputs.Single()).Kind);
    }

    [Fact]
    public async Task Images_are_combined_into_one_pdf()
    {
        var images = new[]
        {
            _fixtures.Image("p1.png", SKEncodedImageFormat.Png),
            _fixtures.Image("p2.webp", SKEncodedImageFormat.Webp),
            _fixtures.Image("p3.jpg", SKEncodedImageFormat.Jpeg),
        };

        var result = await Fixtures.Run(new ImagesToPdfOperation(), images);

        Assert.Equal(3, PageCount(result.Outputs.Single()));
    }
}
