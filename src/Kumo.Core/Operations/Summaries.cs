using Kumo.Core.Files;

namespace Kumo.Core.Operations;

public enum ResultTone
{
    Success,
    Nothing,
    Partial,
    Failure,
    Cancelled,
}

public sealed record ResultSummary(string Headline, string? Detail, ResultTone Tone);

public static class Summaries
{
    public static ResultSummary Describe(BatchResult result)
    {
        int saved = result.Outputs.Count();
        int failed = result.FailedCount;
        var firstFailure = result.FirstFailure;
        string? note = result.Outcomes.Select(o => o.Message).FirstOrDefault(m => m != null);

        if (result.WasCancelled)
        {
            return saved == 0
                ? new ResultSummary("Cancelled", "Nothing was saved.", ResultTone.Cancelled)
                : new ResultSummary($"Cancelled after saving {Files(saved)}", null, ResultTone.Cancelled);
        }

        if (failed == 0)
        {
            return saved == 0
                ? new ResultSummary("Nothing to save", note, ResultTone.Nothing)
                : new ResultSummary($"Saved {Files(saved)}", note, ResultTone.Success);
        }

        if (saved > 0)
            return new ResultSummary($"Saved {Files(saved)}, {failed} failed", firstFailure?.Message, ResultTone.Partial);

        return result.Outcomes.Count == 1
            ? new ResultSummary(firstFailure?.Message ?? "The file couldn't be processed.", null, ResultTone.Failure)
            : new ResultSummary($"{Files(failed)} couldn't be processed", firstFailure?.Message, ResultTone.Failure);
    }

    public static string Selection(IReadOnlyList<InputFile> files)
    {
        if (files.Count == 0)
            return "No files";
        if (files.All(f => f.Kind.IsImage()))
            return files.Count == 1 ? "1 image" : $"{files.Count} images";
        if (files.All(f => f.Kind == FileKind.Pdf))
            return files.Count == 1 ? "1 PDF" : $"{files.Count} PDFs";
        return Files(files.Count);
    }

    public static string Progress(MenuEntry entry, BatchProgress progress)
    {
        string verb = entry.Category switch
        {
            OperationCategory.Convert => "Converting",
            OperationCategory.Crop => "Cropping",
            OperationCategory.Compress => "Compressing",
            OperationCategory.Rotate => "Rotating",
            OperationCategory.Merge => "Combining",
            OperationCategory.Split => "Splitting",
            OperationCategory.Extract => "Extracting",
            OperationCategory.Protect => "Protecting",
            _ => "Working",
        };
        if (progress.Total <= 1)
            return $"{verb}…";
        return $"{verb} {Math.Min(progress.Completed + 1, progress.Total)} of {progress.Total}…";
    }

    /// <summary>Why no action is offered, phrased for the person who dropped the files.</summary>
    public static string Unavailable(IReadOnlyList<InputFile> files, bool includedFolders)
    {
        if (files.Count == 0)
            return includedFolders ? "Kumo works with files, not folders." : "Kumo couldn't read these files.";
        var unknown = files.FirstOrDefault(f => f.Kind == FileKind.Unknown);
        if (unknown != null)
        {
            string extension = Path.GetExtension(unknown.Path).TrimStart('.').ToUpperInvariant();
            return extension.Length > 0 ? $"Kumo doesn't support {extension} files yet." : "Kumo doesn't recognise this kind of file.";
        }
        return files.Select(f => f.Kind).Distinct().Count() > 1
            ? "These files have no action in common. Try them separately."
            : $"There's nothing Kumo can do with {files[0].Kind.DisplayName()} files yet.";
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}
