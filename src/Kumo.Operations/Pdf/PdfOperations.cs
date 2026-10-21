using Kumo.Core.Files;
using Kumo.Core.Operations;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Kumo.Operations.Pdf;

internal abstract class PdfOperation : IFileOperation
{
    public abstract string Id { get; }

    public abstract OperationCategory Category { get; }

    public abstract string Label { get; }

    public abstract string Description { get; }

    public virtual bool IsCombined => false;

    public virtual int MinimumFiles => 1;

    public virtual InputRequirement Requirement => InputRequirement.None;

    public virtual bool Supports(InputFile file) => file.Kind == FileKind.Pdf;

    public abstract Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken);
}

internal sealed class PdfMergeOperation : PdfOperation
{
    public override string Id => "pdf.merge";

    public override OperationCategory Category => OperationCategory.Merge;

    public override string Label => "Merge PDFs";

    public override string Description => "Merge into one PDF (sorted by name)";

    public override bool IsCombined => true;

    public override int MinimumFiles => 2;

    public static IReadOnlyList<InputFile> FindPasswordProtectedInputs(IReadOnlyList<InputFile> inputs)
    {
        var protectedInputs = new List<InputFile>();
        foreach (var input in inputs)
        {
            try
            {
                using var document = PdfFiles.Open(input, OperationLimits.Default);
            }
            catch (OperationFailedException ex) when (ex.Kind == OperationErrorKind.Encrypted)
            {
                protectedInputs.Add(input);
            }
            catch (OperationFailedException)
            {
                // Corrupt and oversized files are reported by the actual merge with the normal operation result.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Read failures are reported by the actual merge with the normal operation result.
            }
        }
        return protectedInputs;
    }

    public override async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var ordered = PdfFiles.NaturalOrder(inputs);
        using var merged = new PdfDocument();
        for (int i = 0; i < ordered.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? password = null;
            context.Options.PdfPasswords?.TryGetValue(ordered[i].Path, out password);
            using var source = PdfFiles.Open(ordered[i], context.Limits, password: password);
            if (merged.PageCount + source.PageCount > context.Limits.MaxPdfPages)
                throw OperationErrors.TooLarge($"The merged document would exceed Kumo's limit of {context.Limits.MaxPdfPages} pages.");
            foreach (var page in source.Pages)
                merged.AddPage(page);
            context.Progress?.Report((i + 1.0) / (ordered.Count + 1));
        }

        var first = ordered[0];
        string path = await PdfFiles.SaveAsync(merged, context.Outputs, context.Outputs.DirectoryFor(first), $"{first.Stem} (merged).pdf",
            inputs.Sum(f => f.Length), cancellationToken).ConfigureAwait(false);
        return OperationResult.Of(path);
    }
}

internal sealed class PdfSplitOperation : PdfOperation
{
    public override string Id => "pdf.split";

    public override OperationCategory Category => OperationCategory.Split;

    public override string Label => "Every page";

    public override string Description => "Split into one PDF per page";

    public override async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var input = inputs[0];
        using var source = PdfFiles.Open(input, context.Limits);
        if (source.PageCount == 1)
            return new OperationResult([], $"{input.Name} has only one page.");

        string folder = context.Outputs.CreateFolder(context.Outputs.DirectoryFor(input), $"{input.Stem} (pages)");
        var outputs = new List<string>();
        try
        {
            for (int i = 0; i < source.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var single = new PdfDocument();
                single.AddPage(source.Pages[i]);
                string name = $"{input.Stem} - page {PdfFiles.PageNumber(i + 1, source.PageCount)}.pdf";
                outputs.Add(await PdfFiles.SaveAsync(single, context.Outputs, folder, name, input.Length / source.PageCount, cancellationToken).ConfigureAwait(false));
                context.Progress?.Report((i + 1.0) / source.PageCount);
            }
        }
        catch
        {
            TryDeleteFolder(folder);
            throw;
        }
        return new OperationResult(outputs);
    }

    internal static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder was created by Kumo for this operation and only holds partial results.
        }
    }
}

internal sealed class PdfExtractPagesOperation : PdfOperation
{
    public override string Id => "pdf.extract.pages";

    public override OperationCategory Category => OperationCategory.Extract;

    public override string Label => "Pages…";

    public override string Description => "Extract selected pages";

    public override InputRequirement Requirement => InputRequirement.PageRange;

    public override async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var input = inputs[0];
        using var source = PdfFiles.Open(input, context.Limits);
        if (!PageRange.TryParse(context.Options.PageRange, source.PageCount, out var pages, out string error))
            throw OperationErrors.InvalidInput($"{input.Name}: {error}");

        using var extracted = new PdfDocument();
        foreach (int page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extracted.AddPage(source.Pages[page - 1]);
        }

        string range = OutputNaming.Sanitize(context.Options.PageRange!.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (range.Length > 40)
            range = range[..40];
        string path = await PdfFiles.SaveAsync(extracted, context.Outputs, context.Outputs.DirectoryFor(input), $"{input.Stem} (pages {range}).pdf",
            input.Length, cancellationToken).ConfigureAwait(false);
        return OperationResult.Of(path);
    }
}

/// <summary>Encrypts a copy with AES-256 (PDF 2.0 standard security handler, revision 6).</summary>
internal sealed class PdfProtectOperation : PdfOperation
{
    public const int MinimumPasswordLength = 4;

    public override string Id => "pdf.protect";

    public override OperationCategory Category => OperationCategory.Protect;

    public override string Label => "Password…";

    public override string Description => "Protect with a password";

    public override InputRequirement Requirement => InputRequirement.Password;

    public override async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var input = inputs[0];
        string password = context.Options.Password ?? string.Empty;
        if (password.Length < MinimumPasswordLength)
            throw OperationErrors.InvalidInput($"Use a password with at least {MinimumPasswordLength} characters.");

        // Modify mode keeps bookmarks, metadata and forms that importing pages into a new document would drop.
        using var protectedCopy = PdfFiles.Open(input, context.Limits, PdfDocumentOpenMode.Modify);
        cancellationToken.ThrowIfCancellationRequested();
        protectedCopy.SecuritySettings.UserPassword = password;
        protectedCopy.SecuritySettings.OwnerPassword = password;
        protectedCopy.SecurityHandler.SetEncryptionToV5();

        string path = await PdfFiles.SaveAsync(protectedCopy, context.Outputs, context.Outputs.DirectoryFor(input), $"{input.Stem} (protected).pdf",
            input.Length, cancellationToken).ConfigureAwait(false);
        return OperationResult.Of(path);
    }
}
