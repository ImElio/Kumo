using Kumo.Core.Files;
using Kumo.Core.Operations;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Kumo.Operations.Pdf;

internal static class PdfFiles
{
    /// <summary>Opens a PDF for importing pages, translating library failures into user-facing errors.</summary>
    public static PdfDocument Open(InputFile file, OperationLimits limits, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Import, string? password = null)
    {
        PdfDocument document;
        try
        {
            document = password is null
                ? PdfReader.Open(file.Path, mode)
                : PdfReader.Open(file.Path, password, mode);
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException or OperationCanceledException))
        {
            if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                throw new OperationFailedException(OperationErrorKind.Encrypted, $"{file.Name} is password-protected.", ex);
            throw OperationErrors.Corrupt($"{file.Name} couldn't be read. It may be damaged or use features Kumo doesn't support.", ex);
        }

        if (document.PageCount == 0)
        {
            document.Dispose();
            throw OperationErrors.Corrupt($"{file.Name} has no pages.");
        }
        if (document.PageCount > limits.MaxPdfPages)
        {
            int count = document.PageCount;
            document.Dispose();
            throw OperationErrors.TooLarge($"{file.Name} has {count} pages, more than Kumo's limit of {limits.MaxPdfPages}.");
        }
        return document;
    }

    public static async Task<string> SaveAsync(PdfDocument document, IOutputWriter outputs, string directory, string fileName, long estimatedBytes, CancellationToken cancellationToken) =>
        await outputs.WriteAsync(directory, fileName, estimatedBytes, (stream, _) =>
        {
            document.Save(stream, closeStream: false);
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);

    public static string PageNumber(int page, int pageCount) => page.ToString(new string('0', Math.Max(3, pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Orders files the way File Explorer sorts names, so "page 2" comes before "page 10".</summary>
    public static IReadOnlyList<InputFile> NaturalOrder(IReadOnlyList<InputFile> files) =>
        files.OrderBy(f => f.Name, NaturalStringComparer.Instance).ToList();
}

internal sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsDigit(x[i]))
                    i++;
                while (j < y.Length && char.IsDigit(y[j]))
                    j++;
                var digitsX = x.AsSpan(startX, i - startX).TrimStart('0');
                var digitsY = y.AsSpan(startY, j - startY).TrimStart('0');
                int byLength = digitsX.Length.CompareTo(digitsY.Length);
                if (byLength != 0)
                    return byLength;
                int byValue = digitsX.CompareTo(digitsY, StringComparison.Ordinal);
                if (byValue != 0)
                    return byValue;
                continue;
            }

            int byChar = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
            if (byChar != 0)
                return byChar;
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
