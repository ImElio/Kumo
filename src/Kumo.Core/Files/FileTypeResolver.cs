namespace Kumo.Core.Files;

public interface IFileTypeResolver
{
    InputFile Resolve(string path);
}

/// <summary>
/// Identifies files by their leading bytes rather than their extension, so a renamed or mislabelled file is
/// routed to the decoder that can actually read it.
/// </summary>
public sealed class SignatureFileTypeResolver : IFileTypeResolver
{
    private const int HeaderLength = 1024;

    public InputFile Resolve(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return new InputFile(path, FileKind.Unknown, 0);

        Span<byte> header = stackalloc byte[HeaderLength];
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            read = stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InputFile(path, FileKind.Unknown, info.Length);
        }
        return new InputFile(path, Detect(header[..read]), info.Length);
    }

    public static FileKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return FileKind.Png;
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return FileKind.Jpeg;
        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return FileKind.WebP;
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
            return FileKind.Gif;
        if (header.StartsWith("II*\0"u8) || header.StartsWith("MM\0*"u8))
            return FileKind.Tiff;
        if (header.Length >= 26 && header.StartsWith("BM"u8) && IsPlausibleBitmapHeader(header))
            return FileKind.Bmp;

        // The PDF specification allows arbitrary bytes before the header; readers accept it within the first KB.
        if (header.IndexOf("%PDF-"u8) >= 0)
            return FileKind.Pdf;
        return FileKind.Unknown;
    }

    private static bool IsPlausibleBitmapHeader(ReadOnlySpan<byte> header)
    {
        uint dibSize = BitConverter.ToUInt32(header[14..18]);
        return dibSize is 12 or 40 or 52 or 56 or 64 or 108 or 124;
    }
}
