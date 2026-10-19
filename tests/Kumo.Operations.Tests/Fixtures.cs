using System.Buffers.Binary;
using Kumo.Core.Diagnostics;
using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Core.Settings;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace Kumo.Operations.Tests;

/// <summary>Creates real image and PDF files in a private temporary folder.</summary>
public sealed class Fixtures : IDisposable
{
    public Fixtures()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("kumo-ops-").FullName;
    }

    public string Directory { get; }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    public InputFile Resolve(string path) => new SignatureFileTypeResolver().Resolve(path);

    /// <summary>Left half red, right half blue, with a transparent top-left pixel block.</summary>
    public InputFile Image(string name, SKEncodedImageFormat format, int width = 64, int height = 32, int quality = 95, bool transparentCorner = false, bool noise = false)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
            if (transparentCorner)
            {
                using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
                canvas.DrawRect(0, 0, 4, 4, clear);
            }
        }
        if (noise)
        {
            var random = new Random(42);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        }

        using var data = bitmap.Encode(format, quality);
        string path = Path.Combine(Directory, name);
        File.WriteAllBytes(path, data.ToArray());
        return Resolve(path);
    }

    /// <summary>A JPEG whose pixels are stored sideways with EXIF orientation 6 ("rotate 90° clockwise to view").</summary>
    public InputFile SidewaysJpeg(string name, int storedWidth, int storedHeight)
    {
        var plain = Image("plain-" + name, SKEncodedImageFormat.Jpeg, storedWidth, storedHeight);
        byte[] jpeg = File.ReadAllBytes(plain.Path);
        File.Delete(plain.Path);

        byte[] tiff =
        [
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        byte[] exif = [.. "Exif\0\0"u8, .. tiff];
        var segment = new byte[4 + exif.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(exif.Length + 2));
        exif.CopyTo(segment, 4);

        string path = Path.Combine(Directory, name);
        File.WriteAllBytes(path, [.. jpeg[..2], .. segment, .. jpeg[2..]]);
        return Resolve(path);
    }

    /// <summary>A structurally valid PNG header announcing an enormous image, followed by no pixel data.</summary>
    public InputFile DecompressionBomb(string name, int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;

        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(stream, "IHDR"u8, ihdr);
        WriteChunk(stream, "IDAT"u8, [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(stream, "IEND"u8, []);
        string path = Path.Combine(Directory, name);
        File.WriteAllBytes(path, stream.ToArray());
        return Resolve(path);
    }

    public InputFile Garbage(string name, ReadOnlySpan<byte> header)
    {
        string path = Path.Combine(Directory, name);
        var bytes = new byte[512];
        new Random(7).NextBytes(bytes);
        header.CopyTo(bytes);
        File.WriteAllBytes(path, bytes);
        return Resolve(path);
    }

    public InputFile Pdf(string name, int pages, string? password = null)
    {
        using var document = new PdfDocument();
        for (int i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(XBrushes.SteelBlue, 40, 40 + i * 20, 200, 100);
        }
        if (password != null)
        {
            document.SecuritySettings.UserPassword = password;
            document.SecuritySettings.OwnerPassword = password;
        }
        string path = Path.Combine(Directory, name);
        document.Save(path);
        return Resolve(path);
    }

    public OutputWriter Writer(params InputFile[] sources) =>
        new(string.Empty, CollisionBehavior.AddNumber, sources.Select(s => s.Path));

    public OperationContext Context(params InputFile[] sources) => new(Writer(sources), OperationOptions.None);

    public OperationContext Context(OperationOptions options, params InputFile[] sources) => new(Writer(sources), options);

    public static async Task<BatchResult> Run(IFileOperation operation, IReadOnlyList<InputFile> files, OperationOptions? options = null, Fixtures? fixtures = null)
    {
        var entry = new MenuEntry(operation.Category, operation.Label, operation.Description, operation.Requirement, [operation]);
        var writer = new OutputWriter(string.Empty, CollisionBehavior.AddNumber, files.Select(f => f.Path));
        return await new OperationExecutor(Log.Null).RunAsync(entry, files, options ?? OperationOptions.None, writer, new ExecutionPolicy(), null, CancellationToken.None);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. type, .. data]));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
