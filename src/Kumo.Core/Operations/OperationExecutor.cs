using Kumo.Core.Diagnostics;
using Kumo.Core.Files;

namespace Kumo.Core.Operations;

public sealed record BatchProgress(int Completed, int Total, string? Current, double Fraction);

public sealed record FileOutcome(string Source, IReadOnlyList<string> Outputs, OperationErrorKind? Error = null, string? Message = null)
{
    public bool Succeeded => Error is null;
}

public sealed record BatchResult(MenuEntry Entry, IReadOnlyList<FileOutcome> Outcomes)
{
    public IEnumerable<string> Outputs => Outcomes.SelectMany(o => o.Outputs);

    public int SucceededCount => Outcomes.Count(o => o.Succeeded);

    public int FailedCount => Outcomes.Count(o => o.Error is not null and not OperationErrorKind.Cancelled);

    public bool WasCancelled => Outcomes.Any(o => o.Error == OperationErrorKind.Cancelled);

    public FileOutcome? FirstFailure => Outcomes.FirstOrDefault(o => o.Error is not null and not OperationErrorKind.Cancelled);
}

public interface IRecycleBin
{
    void Recycle(string path);
}

public sealed record ExecutionPolicy(int MaxConcurrency = 2, bool PreserveOriginals = true, OperationLimits? Limits = null);

public interface IOperationExecutor
{
    Task<BatchResult> RunAsync(MenuEntry entry, IReadOnlyList<InputFile> files, OperationOptions options, IOutputWriter outputs,
        ExecutionPolicy policy, IProgress<BatchProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a menu entry over a selection. Work happens on thread-pool threads with bounded concurrency so a large
/// drop never starts an unbounded number of decoders; each file fails or succeeds on its own.
/// </summary>
public sealed class OperationExecutor : IOperationExecutor
{
    private readonly Log _log;
    private readonly IRecycleBin? _recycleBin;

    public OperationExecutor(Log log, IRecycleBin? recycleBin = null)
    {
        _log = log;
        _recycleBin = recycleBin;
    }

    public async Task<BatchResult> RunAsync(MenuEntry entry, IReadOnlyList<InputFile> files, OperationOptions options, IOutputWriter outputs,
        ExecutionPolicy policy, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        _log.Info("Operation started", ("entry", entry.Description), ("files", files.Count));
        var limits = policy.Limits ?? OperationLimits.Default;

        if (entry.IsCombined)
        {
            progress?.Report(new BatchProgress(0, 1, null, 0));
            var reporter = new FractionReporter(progress, 0, 1, files.Count == 1 ? files[0].Name : null);
            var outcome = await RunOne(entry.Operations[0], files, options, outputs, limits, reporter, cancellationToken).ConfigureAwait(false);
            progress?.Report(new BatchProgress(1, 1, null, 1));
            var combined = new BatchResult(entry, [outcome with { Source = files[0].Path }]);
            LogResult(combined);
            return combined;
        }

        var results = new FileOutcome[files.Count];
        int completed = 0;
        progress?.Report(new BatchProgress(0, files.Count, files.Count > 0 ? files[0].Name : null, 0));
        using var gate = new SemaphoreSlim(Math.Clamp(policy.MaxConcurrency, 1, 8));
        var tasks = files.Select(async (file, index) =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                results[index] = new FileOutcome(file.Path, [], OperationErrorKind.Cancelled, "Cancelled.");
                return;
            }
            try
            {
                var operation = entry.OperationFor(file);
                if (operation == null)
                {
                    results[index] = new FileOutcome(file.Path, [], OperationErrorKind.Unsupported, "This file type isn't supported for this action.");
                    return;
                }
                var reporter = new FractionReporter(progress, Volatile.Read(ref completed), files.Count, file.Name);
                var outcome = await RunOne(operation, [file], options, outputs, limits, reporter, cancellationToken).ConfigureAwait(false);
                if (outcome.Succeeded && outcome.Outputs.Count > 0 && !policy.PreserveOriginals)
                    outcome = RecycleOriginal(file, outcome);
                results[index] = outcome;
            }
            finally
            {
                gate.Release();
                int done = Interlocked.Increment(ref completed);
                progress?.Report(new BatchProgress(done, files.Count, file.Name, (double)done / files.Count));
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var result = new BatchResult(entry, results);
        LogResult(result);
        return result;
    }

    private async Task<FileOutcome> RunOne(IFileOperation operation, IReadOnlyList<InputFile> inputs, OperationOptions options, IOutputWriter outputs,
        OperationLimits limits, IProgress<double> progress, CancellationToken cancellationToken)
    {
        string source = inputs.Count == 1 ? inputs[0].Path : string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var input in inputs)
            {
                if (input.Length > limits.MaxInputBytes)
                    throw OperationErrors.TooLarge($"{input.Name} is larger than Kumo's {limits.MaxInputBytes / (1024 * 1024)} MB limit.");
            }
            var context = new OperationContext(outputs, options, limits, _log, progress);
            var result = await Task.Run(() => operation.ExecuteAsync(inputs, context, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new FileOutcome(source, result.Outputs, Message: result.Note);
        }
        catch (Exception ex)
        {
            var (kind, message) = OperationErrors.Classify(ex);
            if (kind == OperationErrorKind.Unexpected)
                _log.Error("Operation failed", ex, ("operation", operation.Id));
            else if (kind != OperationErrorKind.Cancelled)
                _log.Warning("Operation failed", ("operation", operation.Id), ("kind", kind.ToString()), ("detail", ex.Message));
            return new FileOutcome(source, [], kind, message);
        }
    }

    private FileOutcome RecycleOriginal(InputFile file, FileOutcome outcome)
    {
        if (_recycleBin == null)
            return outcome;
        try
        {
            _recycleBin.Recycle(file.Path);
            return outcome;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _log.Warning("Original could not be moved to the Recycle Bin", ("detail", ex.Message));
            return outcome with { Message = "Saved, but the original could not be moved to the Recycle Bin." };
        }
    }

    private void LogResult(BatchResult result) =>
        _log.Info("Operation finished", ("entry", result.Entry.Description), ("succeeded", result.SucceededCount),
            ("failed", result.FailedCount), ("cancelled", result.WasCancelled));

    private sealed class FractionReporter(IProgress<BatchProgress>? progress, int completed, int total, string? current) : IProgress<double>
    {
        public void Report(double value) =>
            progress?.Report(new BatchProgress(completed, total, current, (completed + Math.Clamp(value, 0, 1)) / Math.Max(1, total)));
    }
}
