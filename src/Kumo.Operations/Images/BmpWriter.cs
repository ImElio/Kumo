using System.Buffers.Binary;
using SkiaSharp;

namespace Kumo.Operations.Images;

/// <summary>Writes 24-bit bottom-up BMP files; Skia can read BMP but has no BMP encoder.</summary>
internal static class BmpWriter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    public static byte[] Write(SKBitmap opaque)
    {
        int width = opaque.Width;
        int height = opaque.Height;
        int stride = (width * 3 + 3) & ~3;
        long imageSize = (long)stride * height;
        long fileSize = FileHeaderSize + InfoHeaderSize + imageSize;
        if (fileSize > int.MaxValue)
            throw new InvalidOperationException("The image is too large for the BMP format.");

        var buffer = new byte[fileSize];
        var span = buffer.AsSpan();
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], (int)fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], FileHeaderSize + InfoHeaderSize);

        var info = span[FileHeaderSize..];
        BinaryPrimitives.WriteInt32LittleEndian(info, InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], height);
        BinaryPrimitives.WriteInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(info[14..], 24);
        BinaryPrimitives.WriteInt32LittleEndian(info[20..], (int)imageSize);
        BinaryPrimitives.WriteInt32LittleEndian(info[24..], 3780);
        BinaryPrimitives.WriteInt32LittleEndian(info[28..], 3780);

        if (opaque.ColorType != SKColorType.Bgra8888)
            throw new InvalidOperationException("BMP output expects BGRA pixels.");

        var pixels = span[(FileHeaderSize + InfoHeaderSize)..];
        var source = opaque.GetPixelSpan();
        int sourceStride = opaque.RowBytes;
        for (int y = 0; y < height; y++)
        {
            var row = pixels.Slice((height - 1 - y) * stride, stride);
            var sourceRow = source.Slice(y * sourceStride, width * 4);
            for (int x = 0; x < width; x++)
            {
                row[x * 3] = sourceRow[x * 4];
                row[x * 3 + 1] = sourceRow[x * 4 + 1];
                row[x * 3 + 2] = sourceRow[x * 4 + 2];
            }
        }
        return buffer;
    }
}
