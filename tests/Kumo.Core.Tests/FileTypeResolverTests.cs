using Kumo.Core.Files;

namespace Kumo.Core.Tests;

public sealed class FileTypeResolverTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-types-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static TheoryData<byte[], FileKind> Signatures => new()
    {
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0], FileKind.Png },
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10], FileKind.Jpeg },
        { "RIFF\x24\0\0\0WEBPVP8 "u8.ToArray(), FileKind.WebP },
        { "GIF89a\x01\0"u8.ToArray(), FileKind.Gif },
        { "II*\0\x08\0"u8.ToArray(), FileKind.Tiff },
        { "%PDF-1.7\n%"u8.ToArray(), FileKind.Pdf },
        { "\r\n\r\n%PDF-1.4"u8.ToArray(), FileKind.Pdf },
        { "RIFF\x24\0\0\0WAVEfmt "u8.ToArray(), FileKind.Unknown },
        { "hello world"u8.ToArray(), FileKind.Unknown },
        { [], FileKind.Unknown },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Detects_formats_by_content(byte[] header, FileKind expected)
    {
        Assert.Equal(expected, SignatureFileTypeResolver.Detect(header));
    }

    [Fact]
    public void Bitmap_requires_a_known_header_size()
    {
        var header = new byte[30];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(40u).CopyTo(header, 14);
        Assert.Equal(FileKind.Bmp, SignatureFileTypeResolver.Detect(header));

        BitConverter.GetBytes(7u).CopyTo(header, 14);
        Assert.Equal(FileKind.Unknown, SignatureFileTypeResolver.Detect(header));
    }

    [Fact]
    public void Extension_is_ignored_in_favour_of_content()
    {
        string path = Path.Combine(_directory, "actually-a-pdf.png");
        File.WriteAllBytes(path, "%PDF-1.7\n"u8.ToArray());

        var file = new SignatureFileTypeResolver().Resolve(path);

        Assert.Equal(FileKind.Pdf, file.Kind);
        Assert.Equal(9, file.Length);
    }

    [Fact]
    public void Missing_file_is_unknown()
    {
        var file = new SignatureFileTypeResolver().Resolve(Path.Combine(_directory, "missing.png"));
        Assert.Equal(FileKind.Unknown, file.Kind);
    }
}
