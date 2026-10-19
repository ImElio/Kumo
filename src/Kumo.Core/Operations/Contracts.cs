using Kumo.Core.Diagnostics;
using Kumo.Core.Files;

namespace Kumo.Core.Operations;

/// <summary>Top-level entries of the radial menu, in the order they are laid out clockwise.</summary>
public enum OperationCategory
{
    Convert,
    Resize,
    Compress,
    Rotate,
    Merge,
    Split,
    Extract,
    Protect,
}

public enum InputRequirement
{
    None,
    PageRange,
    Password,
    Dimensions,
}

/// <param name="ScaleX">Horizontal scale chosen in the resize editor; each image in a batch gets the same proportion.</param>
/// <param name="ScaleY">Vertical scale; equal to <paramref name="ScaleX"/> unless the aspect ratio was unlocked.</param>
public sealed record OperationOptions(string? PageRange = null, string? Password = null, double? ScaleX = null, double? ScaleY = null)
{
    public static OperationOptions None { get; } = new();
}

/// <param name="Outputs">Files written, in a meaningful order.</param>
/// <param name="Note">Optional explanation when the operation deliberately produced nothing.</param>
public sealed record OperationResult(IReadOnlyList<string> Outputs, string? Note = null)
{
    public static OperationResult Of(params string[] outputs) => new(outputs);
}

public sealed record OperationLimits(
    long MaxInputBytes = 1L << 30,
    long MaxImagePixels = 200_000_000,
    int MaxPdfPages = 5_000,
    int MaxRenderedPages = 500,
    int MaxRenderedEdge = 8_000)
{
    public static OperationLimits Default { get; } = new();
}

public sealed class OperationContext
{
    public OperationContext(IOutputWriter outputs, OperationOptions options, OperationLimits? limits = null, Log? log = null, IProgress<double>? progress = null)
    {
        Outputs = outputs;
        Options = options;
        Limits = limits ?? OperationLimits.Default;
        Log = log ?? Log.Null;
        Progress = progress;
    }

    public IOutputWriter Outputs { get; }

    public OperationOptions Options { get; }

    public OperationLimits Limits { get; }

    public Log Log { get; }

    /// <summary>Fraction of the current item completed, for operations that can report it.</summary>
    public IProgress<double>? Progress { get; }
}

/// <summary>
/// A concrete file operation. Per-file operations receive one input at a time; combined operations
/// (such as merging) receive the whole selection once.
/// </summary>
public interface IFileOperation
{
    string Id { get; }

    OperationCategory Category { get; }

    /// <summary>Short label shown on the radial segment within its category, for example "WEBP".</summary>
    string Label { get; }

    /// <summary>Full phrase used for status text and screen readers, for example "Convert to WEBP".</summary>
    string Description { get; }

    bool IsCombined { get; }

    InputRequirement Requirement { get; }

    bool Supports(InputFile file);

    bool SupportsSelection(IReadOnlyList<InputFile> files) =>
        files.Count > 0 && files.All(Supports) && (!IsCombined || files.Count >= MinimumFiles);

    int MinimumFiles => 1;

    Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken);
}
