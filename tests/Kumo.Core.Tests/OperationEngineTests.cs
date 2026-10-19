using Kumo.Core.Diagnostics;
using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Core.Radial;
using Kumo.Core.Settings;

namespace Kumo.Core.Tests;

public sealed class OperationEngineTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-engine-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private InputFile CreateFile(string name, FileKind kind)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, name);
        return new InputFile(path, kind, new FileInfo(path).Length);
    }

    private static readonly FakeOperation PngToJpg = new("image.jpg", OperationCategory.Convert, "JPG", f => f.Kind is FileKind.Png or FileKind.WebP);
    private static readonly FakeOperation PngToWebp = new("image.webp", OperationCategory.Convert, "WEBP", f => f.Kind is FileKind.Png or FileKind.Jpeg);
    private static readonly FakeOperation ImageToPng = new("image.png", OperationCategory.Convert, "PNG", f => f.Kind is FileKind.Jpeg or FileKind.WebP);
    private static readonly FakeOperation PdfToPng = new("pdf.png", OperationCategory.Convert, "PNG", f => f.Kind == FileKind.Pdf);
    private static readonly FakeOperation Rotate = new("image.rotate", OperationCategory.Rotate, "90°", f => f.Kind.IsImage());
    private static readonly FakeOperation MergePdf = new("pdf.merge", OperationCategory.Merge, "Merge PDFs", f => f.Kind == FileKind.Pdf, combined: true, minimumFiles: 2);

    private static OperationRegistry Registry() => new([PngToJpg, PngToWebp, ImageToPng, PdfToPng, Rotate, MergePdf]);

    [Fact]
    public void Only_entries_valid_for_every_file_are_offered()
    {
        var entries = Registry().EntriesFor([
            new InputFile("a.png", FileKind.Png, 1),
            new InputFile("b.jpg", FileKind.Jpeg, 1),
        ]);

        Assert.Equal(["WEBP", "90°"], entries.Select(e => e.Label));
    }

    [Fact]
    public void Operations_sharing_a_label_are_grouped_into_one_entry()
    {
        var entries = Registry().EntriesFor([
            new InputFile("a.jpg", FileKind.Jpeg, 1),
            new InputFile("b.pdf", FileKind.Pdf, 1),
        ]);

        var png = Assert.Single(entries);
        Assert.Equal("PNG", png.Label);
        Assert.Equal([ImageToPng, PdfToPng], png.Operations);
        Assert.Same(PdfToPng, png.OperationFor(new InputFile("c.pdf", FileKind.Pdf, 1)));
    }

    [Fact]
    public void Combined_operations_respect_their_minimum_selection()
    {
        var single = Registry().EntriesFor([new InputFile("a.pdf", FileKind.Pdf, 1)]);
        var pair = Registry().EntriesFor([new InputFile("a.pdf", FileKind.Pdf, 1), new InputFile("b.pdf", FileKind.Pdf, 1)]);

        Assert.DoesNotContain(single, e => e.Category == OperationCategory.Merge);
        Assert.Contains(pair, e => e.Category == OperationCategory.Merge && e.IsCombined);
    }

    [Fact]
    public void Unknown_files_disable_every_entry()
    {
        Assert.Empty(Registry().EntriesFor([new InputFile("a.png", FileKind.Png, 1), new InputFile("x.bin", FileKind.Unknown, 1)]));
    }

    [Fact]
    public void Duplicate_operation_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new OperationRegistry([Rotate, Rotate]));
    }

    [Fact]
    public async Task Batch_reports_per_file_failures_without_stopping_the_rest()
    {
        var operation = new FakeOperation("x", OperationCategory.Convert, "X")
        {
            Failure = f => f.Name == "bad.png" ? OperationErrors.Corrupt("The image is damaged.") : null,
        };
        var files = new[] { CreateFile("a.png", FileKind.Png), CreateFile("bad.png", FileKind.Png), CreateFile("c.png", FileKind.Png) };
        var entry = new MenuEntry(OperationCategory.Convert, "X", "Convert", InputRequirement.None, [operation]);
        var progress = new List<BatchProgress>();

        var result = await new OperationExecutor(Log.Null).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, files.Select(f => f.Path)), new ExecutionPolicy(),
            new SynchronousProgress<BatchProgress>(progress.Add), CancellationToken.None);

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(OperationErrorKind.Corrupt, result.FirstFailure!.Error);
        Assert.Equal("The image is damaged.", result.FirstFailure.Message);
        Assert.Equal(2, result.Outputs.Count());
        Assert.Equal(3, progress.Max(p => p.Completed));
        Assert.True(File.Exists(Path.Combine(_directory, "a.out")));
    }

    [Fact]
    public async Task Unexpected_exceptions_are_turned_into_friendly_messages()
    {
        var operation = new FakeOperation("x", OperationCategory.Convert, "X") { Failure = _ => new NullReferenceException("internal detail") };
        var files = new[] { CreateFile("a.png", FileKind.Png) };
        var entry = new MenuEntry(OperationCategory.Convert, "X", "Convert", InputRequirement.None, [operation]);

        var result = await new OperationExecutor(Log.Null).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(), null, CancellationToken.None);

        Assert.Equal(OperationErrorKind.Unexpected, result.FirstFailure!.Error);
        Assert.DoesNotContain("internal detail", result.FirstFailure.Message);
    }

    [Fact]
    public async Task Concurrency_is_bounded()
    {
        var operation = new FakeOperation("x", OperationCategory.Convert, "X") { Delay = TimeSpan.FromMilliseconds(40) };
        var files = Enumerable.Range(0, 8).Select(i => CreateFile($"f{i}.png", FileKind.Png)).ToArray();
        var entry = new MenuEntry(OperationCategory.Convert, "X", "Convert", InputRequirement.None, [operation]);

        var result = await new OperationExecutor(Log.Null).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(MaxConcurrency: 2), null, CancellationToken.None);

        Assert.Equal(8, result.SucceededCount);
        Assert.InRange(operation.MaxObservedConcurrency, 1, 2);
    }

    [Fact]
    public async Task Cancellation_marks_unfinished_files_and_writes_nothing_for_them()
    {
        var operation = new FakeOperation("x", OperationCategory.Convert, "X") { Delay = TimeSpan.FromSeconds(5) };
        var files = Enumerable.Range(0, 4).Select(i => CreateFile($"f{i}.png", FileKind.Png)).ToArray();
        var entry = new MenuEntry(OperationCategory.Convert, "X", "Convert", InputRequirement.None, [operation]);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await new OperationExecutor(Log.Null).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(MaxConcurrency: 1), null, cancel.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(0, result.FailedCount);
        Assert.All(result.Outcomes, o => Assert.Equal(OperationErrorKind.Cancelled, o.Error));
        Assert.Empty(Directory.GetFiles(_directory, "*.out"));
    }

    [Fact]
    public async Task Oversized_inputs_are_rejected_before_processing()
    {
        var operation = new FakeOperation("x", OperationCategory.Convert, "X");
        var file = CreateFile("huge.png", FileKind.Png) with { Length = 10_000 };
        var entry = new MenuEntry(OperationCategory.Convert, "X", "Convert", InputRequirement.None, [operation]);

        var result = await new OperationExecutor(Log.Null).RunAsync(entry, [file], OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(Limits: new OperationLimits(MaxInputBytes: 1_000)), null, CancellationToken.None);

        Assert.Equal(OperationErrorKind.TooLarge, result.FirstFailure!.Error);
    }

    [Fact]
    public async Task Originals_are_recycled_only_when_requested_and_only_after_success()
    {
        var operation = new FakeOperation("x", OperationCategory.Convert, "X") { Failure = f => f.Name == "bad.png" ? OperationErrors.Corrupt("damaged") : null };
        var files = new[] { CreateFile("good.png", FileKind.Png), CreateFile("bad.png", FileKind.Png) };
        var entry = new MenuEntry(OperationCategory.Convert, "X", "Convert", InputRequirement.None, [operation]);
        var bin = new RecordingRecycleBin();

        await new OperationExecutor(Log.Null, bin).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(PreserveOriginals: false), null, CancellationToken.None);
        await new OperationExecutor(Log.Null, bin).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(PreserveOriginals: true), null, CancellationToken.None);

        Assert.Equal([files[0].Path], bin.Recycled);
    }

    [Fact]
    public async Task Combined_operations_run_once_with_the_whole_selection()
    {
        var files = new[] { CreateFile("a.pdf", FileKind.Pdf), CreateFile("b.pdf", FileKind.Pdf) };
        var entry = Registry().EntriesFor(files).Single(e => e.IsCombined);

        var result = await new OperationExecutor(Log.Null).RunAsync(entry, files, OperationOptions.None,
            new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []), new ExecutionPolicy(), null, CancellationToken.None);

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(2, outcome.Outputs.Count);
    }

    [Fact]
    public void Radial_root_groups_categories_and_promotes_single_entries()
    {
        var entries = Registry().EntriesFor([new InputFile("a.png", FileKind.Png, 1)]);
        var model = new RadialMenuModel(entries);

        Assert.Equal(["Convert", "Rotate"], model.Items.Select(i => i.Label));
        Assert.Equal(RadialItemKind.Category, model.Items[0].Kind);
        Assert.Equal(RadialItemKind.Action, model.Items[1].Kind);
    }

    [Fact]
    public void Radial_submenu_opens_executes_and_goes_back()
    {
        var model = new RadialMenuModel(Registry().EntriesFor([new InputFile("a.png", FileKind.Png, 1)]));

        Assert.False(model.Back());
        Assert.Equal(RadialActivationKind.OpenedSubmenu, model.Activate(0).Kind);
        Assert.False(model.IsAtRoot);
        Assert.Equal("Convert", model.Title);
        Assert.Equal(["JPG", "WEBP"], model.Items.Select(i => i.Label));

        var activation = model.Activate(1);
        Assert.Equal(RadialActivationKind.Execute, activation.Kind);
        Assert.Equal("WEBP", activation.Entry!.Label);

        Assert.True(model.Back());
        Assert.True(model.IsAtRoot);
        Assert.Equal(RadialActivationKind.None, model.Activate(7).Kind);
    }

    [Fact]
    public void Single_convert_target_is_shown_directly_at_the_root()
    {
        var model = new RadialMenuModel(Registry().EntriesFor([new InputFile("a.pdf", FileKind.Pdf, 1)]));

        var item = Assert.Single(model.Items);
        Assert.Equal("To PNG", item.Label);
        Assert.Equal(RadialActivationKind.Execute, model.Activate(0).Kind);
    }

    [Fact]
    public void Empty_selection_gives_an_empty_menu()
    {
        Assert.True(new RadialMenuModel([]).IsEmpty);
    }

    private sealed class RecordingRecycleBin : IRecycleBin
    {
        public List<string> Recycled { get; } = [];

        public void Recycle(string path) => Recycled.Add(path);
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly object _gate = new();

        public void Report(T value)
        {
            lock (_gate)
                report(value);
        }
    }
}
