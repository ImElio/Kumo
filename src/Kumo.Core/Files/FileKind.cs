namespace Kumo.Core.Files;

public enum FileKind
{
    Unknown,
    Png,
    Jpeg,
    WebP,
    Bmp,
    Gif,
    Tiff,
    Pdf,
}

public sealed record InputFile(string Path, FileKind Kind, long Length)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Path);

    public string? Directory => System.IO.Path.GetDirectoryName(Path);
}

public static class FileKinds
{
    public static bool IsImage(this FileKind kind) => kind is FileKind.Png or FileKind.Jpeg or FileKind.WebP or FileKind.Bmp or FileKind.Gif or FileKind.Tiff;

    public static string Extension(this FileKind kind) => kind switch
    {
        FileKind.Png => ".png",
        FileKind.Jpeg => ".jpg",
        FileKind.WebP => ".webp",
        FileKind.Bmp => ".bmp",
        FileKind.Gif => ".gif",
        FileKind.Tiff => ".tif",
        FileKind.Pdf => ".pdf",
        _ => string.Empty,
    };

    public static string DisplayName(this FileKind kind) => kind switch
    {
        FileKind.Jpeg => "JPG",
        FileKind.WebP => "WEBP",
        FileKind.Unknown => "Unknown",
        _ => kind.ToString().ToUpperInvariant(),
    };
}
