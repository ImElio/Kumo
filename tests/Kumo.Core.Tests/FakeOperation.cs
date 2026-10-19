using System.Text;
using Kumo.Core.Files;
using Kumo.Core.Operations;

namespace Kumo.Core.Tests;

internal sealed class FakeOperation : IFileOperation
{
    private int _running;

    public FakeOperation(string id, OperationCategory category, string label, Func<InputFile, bool>? supports = null, bool combined = false, int minimumFiles = 1)
    {
        Id = id;
        Category = category;
        Label = label;
        Supported = supports ?? (_ => true);
        IsCombined = combined;
        MinimumFiles = minimumFiles;
    }

    public string Id { get; }

    public OperationCategory Category { get; }

    public string Label { get; }

    public string Description => $"{Category} {Label}";

    public bool IsCombined { get; }

    public int MinimumFiles { get; }

    public InputRequirement Requirement => InputRequirement.None;

    public Func<InputFile, bool> Supported { get; }

    public Func<InputFile, Exception?> Failure { get; set; } = _ => null;

    public TimeSpan Delay { get; set; }

    public int MaxObservedConcurrency { get; private set; }

    public bool Supports(InputFile file) => Supported(file);

    public async Task<OperationResult> ExecuteAsync(IReadOnlyList<InputFile> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        int running = Interlocked.Increment(ref _running);
        lock (this)
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, running);
        try
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            var outputs = new List<string>();
            foreach (var input in inputs)
            {
                if (Failure(input) is { } failure)
                    throw failure;
                outputs.Add(await context.Outputs.WriteAsync(context.Outputs.DirectoryFor(input), input.Stem + ".out", 10,
                    (stream, ct) => stream.WriteAsync(Encoding.UTF8.GetBytes(input.Name), ct).AsTask(), cancellationToken));
            }
            return new OperationResult(outputs);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}
