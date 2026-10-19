using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Operations.Images;
using SkiaSharp;

namespace Kumo.Operations.Tests;

public sealed class ImageOperationTests : IDisposable
{
    private readonly Fixtures _fixtures = new();

    public void Dispose() => _fixtures.Dispose();

    private static SKBitmap Load(string path) => SKBitmap.Decode(path) ?? throw new InvalidDataException(path);

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "a.png", FileKind.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png, "b.png", FileKind.WebP)]
    [InlineData(SKEncodedImageFormat.Png, "c.png", FileKind.Bmp)]
    [InlineData(SKEncodedImageFormat.Jpeg, "d.jpg", FileKind.Png)]
    [InlineData(SKEncodedImageFormat.Webp, "e.webp", FileKind.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg, "f.jpg", FileKind.WebP)]
    public async Task Conversion_produces_a_valid_file_of_the_target_format(SKEncodedImageFormat sourceFormat, string name, FileKind target)
    {
        var source = _fixtures.Image(name, sourceFormat);

        var result = await Fixtures.Run(new ImageConvertOperation(target), [source]);

        string output = Assert.Single(result.Outputs);
        Assert.Equal(target.Extension(), Path.GetExtension(output));
        Assert.Equal(target, _fixtures.Resolve(output).Kind);
        using var bitmap = Load(output);
        Assert.Equal((64, 32), (bitmap.Width, bitmap.Height));
        AssertClose(SKColors.Red, bitmap.GetPixel(8, 16));
        AssertClose(SKColors.Blue, bitmap.GetPixel(56, 16));
        Assert.True(File.Exists(source.Path));
    }

    [Fact]
    public async Task Transparency_is_flattened_onto_white_for_jpeg()
    {
        var source = _fixtures.Image("alpha.png", SKEncodedImageFormat.Png, transparentCorner: true);

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.Jpeg), [source]);

        using var bitmap = Load(result.Outputs.Single());
        AssertClose(SKColors.White, bitmap.GetPixel(1, 1));
    }

    [Fact]
    public async Task Bmp_output_keeps_exact_colours()
    {
        var source = _fixtures.Image("exact.png", SKEncodedImageFormat.Png, width: 7, height: 3);

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.Bmp), [source]);

        using var bitmap = Load(result.Outputs.Single());
        Assert.Equal((7, 3), (bitmap.Width, bitmap.Height));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(6, 2));
    }

    [Fact]
    public void Exif_orientation_is_applied_when_decoding()
    {
        var source = _fixtures.SidewaysJpeg("sideways.jpg", storedWidth: 40, storedHeight: 20);

        using var bitmap = ImageCodec.Decode(source, OperationLimits.Default, CancellationToken.None);

        Assert.Equal((20, 40), (bitmap.Width, bitmap.Height));
        AssertClose(SKColors.Red, bitmap.GetPixel(10, 5));
        AssertClose(SKColors.Blue, bitmap.GetPixel(10, 35));
    }

    [Fact]
    public void Same_format_conversion_is_not_offered()
    {
        var source = _fixtures.Image("same.png", SKEncodedImageFormat.Png);
        Assert.False(new ImageConvertOperation(FileKind.Png).Supports(source));
        Assert.True(new ImageConvertOperation(FileKind.Jpeg).Supports(source));
    }

    [Theory]
    [InlineData(0.5, 0.5, 400, 200, 200, 100)]
    [InlineData(0.25, 0.25, 400, 200, 100, 50)]
    [InlineData(1.5, 1.5, 40, 20, 60, 30)]
    [InlineData(0.5, 1.0, 400, 200, 200, 200)]
    public async Task Resize_applies_the_chosen_scale(double scaleX, double scaleY, int width, int height, int expectedWidth, int expectedHeight)
    {
        var source = _fixtures.Image($"resize-{scaleX}-{scaleY}-{width}.png", SKEncodedImageFormat.Png, width, height);

        var result = await Fixtures.Run(new ImageResizeOperation(), [source], new OperationOptions(ScaleX: scaleX, ScaleY: scaleY));

        string output = result.Outputs.Single();
        using var bitmap = Load(output);
        Assert.Equal((expectedWidth, expectedHeight), (bitmap.Width, bitmap.Height));
        Assert.EndsWith($"({expectedWidth}×{expectedHeight}).png", output);
        AssertClose(SKColors.Red, bitmap.GetPixel(expectedWidth / 4, expectedHeight / 2));
    }

    [Fact]
    public async Task Batch_resize_keeps_each_image_proportional()
    {
        var wide = _fixtures.Image("wide.png", SKEncodedImageFormat.Png, 400, 200);
        var tall = _fixtures.Image("tall.png", SKEncodedImageFormat.Png, 100, 300);

        var result = await Fixtures.Run(new ImageResizeOperation(), [wide, tall], new OperationOptions(ScaleX: 0.5, ScaleY: 0.5));

        Assert.Equal(2, result.SucceededCount);
        using var first = Load(result.Outcomes[0].Outputs.Single());
        using var second = Load(result.Outcomes[1].Outputs.Single());
        Assert.Equal((200, 100), (first.Width, first.Height));
        Assert.Equal((50, 150), (second.Width, second.Height));
    }

    [Fact]
    public async Task Resize_to_the_same_size_saves_nothing()
    {
        var source = _fixtures.Image("same-size.png", SKEncodedImageFormat.Png, 64, 32);

        var result = await Fixtures.Run(new ImageResizeOperation(), [source], new OperationOptions(ScaleX: 1, ScaleY: 1));

        var outcome = Assert.Single(result.Outcomes);
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Outputs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task Resize_rejects_invalid_scales(double scale)
    {
        var source = _fixtures.Image($"bad-scale-{scale}.png", SKEncodedImageFormat.Png);

        var result = await Fixtures.Run(new ImageResizeOperation(), [source], new OperationOptions(ScaleX: scale, ScaleY: scale));

        Assert.Equal(OperationErrorKind.InvalidInput, result.FirstFailure!.Error);
    }

    [Fact]
    public void Preview_reports_full_size_and_is_downscaled()
    {
        var source = _fixtures.Image("preview.png", SKEncodedImageFormat.Png, 1600, 800);

        var preview = ImagePreview.Load(source, 400);

        Assert.Equal((1600, 800), (preview.Width, preview.Height));
        using var bitmap = SKBitmap.Decode(preview.Png);
        Assert.Equal((400, 200), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public void Preview_follows_exif_orientation()
    {
        var source = _fixtures.SidewaysJpeg("preview-sideways.jpg", 40, 20);

        var preview = ImagePreview.Load(source, 400);

        Assert.Equal((20, 40), (preview.Width, preview.Height));
    }

    [Theory]
    [InlineData(90, 32, 64)]
    [InlineData(270, 32, 64)]
    [InlineData(180, 64, 32)]
    public async Task Rotation_swaps_dimensions_and_moves_pixels(int degrees, int expectedWidth, int expectedHeight)
    {
        var source = _fixtures.Image($"rotate-{degrees}.png", SKEncodedImageFormat.Png);

        var result = await Fixtures.Run(new ImageRotateOperation(degrees), [source]);

        using var bitmap = Load(result.Outputs.Single());
        Assert.Equal((expectedWidth, expectedHeight), (bitmap.Width, bitmap.Height));
        var redCorner = degrees switch
        {
            90 => bitmap.GetPixel(expectedWidth - 2, 2),
            270 => bitmap.GetPixel(2, expectedHeight - 2),
            _ => bitmap.GetPixel(expectedWidth - 2, 2),
        };
        AssertClose(SKColors.Red, redCorner);
        Assert.EndsWith("(rotated).png", result.Outputs.Single());
    }

    [Fact]
    public async Task Compression_makes_a_detailed_jpeg_smaller()
    {
        var source = _fixtures.Image("detailed.jpg", SKEncodedImageFormat.Jpeg, 256, 256, quality: 100, noise: true);

        var result = await Fixtures.Run(new ImageCompressOperation(CompressionLevel.Balanced), [source]);

        string output = result.Outputs.Single();
        Assert.True(new FileInfo(output).Length < source.Length);
        Assert.Equal(FileKind.Jpeg, _fixtures.Resolve(output).Kind);
    }

    [Fact]
    public async Task Compression_that_would_grow_the_file_saves_nothing()
    {
        var source = _fixtures.Image("tiny.jpg", SKEncodedImageFormat.Jpeg, 256, 256, quality: 10, noise: true);

        var result = await Fixtures.Run(new ImageCompressOperation(CompressionLevel.High), [source]);

        var outcome = Assert.Single(result.Outcomes);
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Outputs);
        Assert.Single(Directory.GetFiles(_fixtures.Directory));
    }

    [Fact]
    public void Lossless_compression_applies_only_to_png()
    {
        var png = _fixtures.Image("l.png", SKEncodedImageFormat.Png);
        var jpg = _fixtures.Image("l.jpg", SKEncodedImageFormat.Jpeg);

        Assert.True(new ImageCompressOperation(CompressionLevel.Lossless).Supports(png));
        Assert.False(new ImageCompressOperation(CompressionLevel.Lossless).Supports(jpg));
        Assert.False(new ImageCompressOperation(CompressionLevel.Balanced).Supports(png));
    }

    [Fact]
    public async Task Corrupt_images_fail_cleanly()
    {
        var source = _fixtures.Garbage("broken.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.Jpeg), [source]);

        Assert.Equal(OperationErrorKind.Corrupt, result.FirstFailure!.Error);
        Assert.Single(Directory.GetFiles(_fixtures.Directory));
    }

    [Fact]
    public async Task Decompression_bombs_are_rejected_before_allocation()
    {
        var bomb = _fixtures.DecompressionBomb("bomb.png", 60_000, 60_000);
        Assert.True(bomb.Length < 200);

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.Jpeg), [bomb]);

        Assert.Equal(OperationErrorKind.TooLarge, result.FirstFailure!.Error);
    }

    [Fact]
    public async Task Locked_files_are_reported_as_in_use()
    {
        var source = _fixtures.Image("locked.png", SKEncodedImageFormat.Png);
        using var lockHandle = new FileStream(source.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.Jpeg), [source]);

        Assert.Equal(OperationErrorKind.Locked, result.FirstFailure!.Error);
    }

    [Fact]
    public async Task Existing_outputs_are_not_overwritten()
    {
        var source = _fixtures.Image("photo.png", SKEncodedImageFormat.Png);
        File.WriteAllText(Path.Combine(_fixtures.Directory, "photo.jpg"), "keep me");

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.Jpeg), [source]);

        Assert.EndsWith("photo (1).jpg", result.Outputs.Single());
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_fixtures.Directory, "photo.jpg")));
    }

    [Fact]
    public async Task Batch_of_images_converts_each_file()
    {
        var files = Enumerable.Range(1, 6).Select(i => _fixtures.Image($"batch{i}.png", SKEncodedImageFormat.Png)).ToArray();

        var result = await Fixtures.Run(new ImageConvertOperation(FileKind.WebP), files);

        Assert.Equal(6, result.SucceededCount);
        Assert.All(result.Outputs, o => Assert.Equal(FileKind.WebP, _fixtures.Resolve(o).Kind));
    }

    private static void AssertClose(SKColor expected, SKColor actual, int tolerance = 40)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance
            && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"Expected {expected} but got {actual}.");
    }
}
