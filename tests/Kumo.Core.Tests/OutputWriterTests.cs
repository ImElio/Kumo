using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Core.Settings;

namespace Kumo.Core.Tests;

public sealed class OutputWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-output-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static Func<Stream, CancellationToken, Task> Text(string value) =>
        (stream, ct) => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(value), ct).AsTask();

    [Theory]
    [InlineData("photo.webp", 0, "photo.webp")]
    [InlineData("photo.webp", 1, "photo (1).webp")]
    [InlineData("archive.tar.gz", 3, "archive.tar (3).gz")]
    [InlineData("README", 2, "README (2)")]
    public void Candidates_append_a_number_before_the_extension(string name, int attempt, string expected)
    {
        Assert.Equal(expected, OutputNaming.Candidate(name, attempt));
    }

    [Theory]
    [InlineData("a:b?.png", "a_b_.png")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("", "output")]
    public void Names_are_sanitized(string name, string expected)
    {
        Assert.Equal(expected, OutputNaming.Sanitize(name));
    }

    [Fact]
    public async Task Existing_files_are_never_overwritten_by_default()
    {
        File.WriteAllText(Path.Combine(_directory, "photo.webp"), "existing");
        var writer = new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []);

        string first = await writer.WriteAsync(_directory, "photo.webp", 1, Text("one"), CancellationToken.None);
        string second = await writer.WriteAsync(_directory, "photo.webp", 1, Text("two"), CancellationToken.None);

        Assert.Equal(Path.Combine(_directory, "photo (1).webp"), first);
        Assert.Equal(Path.Combine(_directory, "photo (2).webp"), second);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_directory, "photo.webp")));
    }

    [Fact]
    public async Task Replace_mode_replaces_unrelated_files_but_never_a_source()
    {
        string source = Path.Combine(_directory, "photo.png");
        File.WriteAllText(source, "original");
        File.WriteAllText(Path.Combine(_directory, "photo.webp"), "old result");
        var writer = new OutputWriter(string.Empty, CollisionBehavior.ReplaceOutput, [source]);

        string replaced = await writer.WriteAsync(_directory, "photo.webp", 1, Text("new"), CancellationToken.None);
        string protectedSource = await writer.WriteAsync(_directory, "photo.png", 1, Text("rotated"), CancellationToken.None);

        Assert.Equal(Path.Combine(_directory, "photo.webp"), replaced);
        Assert.Equal("new", File.ReadAllText(replaced));
        Assert.Equal(Path.Combine(_directory, "photo (1).png"), protectedSource);
        Assert.Equal("original", File.ReadAllText(source));
    }

    [Fact]
    public async Task Replace_mode_does_not_overwrite_an_output_from_the_same_batch()
    {
        var writer = new OutputWriter(string.Empty, CollisionBehavior.ReplaceOutput, []);

        string first = await writer.WriteAsync(_directory, "a.webp", 1, Text("from png"), CancellationToken.None);
        string second = await writer.WriteAsync(_directory, "a.webp", 1, Text("from jpg"), CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.Equal("from png", File.ReadAllText(first));
    }

    [Fact]
    public async Task Failure_while_writing_leaves_no_files()
    {
        var writer = new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []);

        await Assert.ThrowsAsync<InvalidDataException>(() => writer.WriteAsync(_directory, "broken.png", 1, async (stream, ct) =>
        {
            await stream.WriteAsync(new byte[100], ct);
            throw new InvalidDataException("decoder failed");
        }, CancellationToken.None));

        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task Cancellation_leaves_no_files()
    {
        var writer = new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []);
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(_directory, "late.png", 1, async (stream, ct) =>
        {
            await stream.WriteAsync(new byte[100], ct);
            cancel.Cancel();
            ct.ThrowIfCancellationRequested();
        }, cancel.Token));

        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task Insufficient_space_is_reported_before_writing()
    {
        var writer = new OutputWriter(string.Empty, CollisionBehavior.AddNumber, [], freeSpace: _ => 1024);

        var error = await Assert.ThrowsAsync<OperationFailedException>(() => writer.WriteAsync(_directory, "big.png", 50_000_000, Text("x"), CancellationToken.None));

        Assert.Equal(OperationErrorKind.DiskFull, error.Kind);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public void Output_folder_setting_overrides_the_source_folder()
    {
        string target = Directory.CreateDirectory(Path.Combine(_directory, "results")).FullName;
        var source = new InputFile(@"C:\photos\a.png", FileKind.Png, 1);

        Assert.Equal(target, new OutputWriter(target, CollisionBehavior.AddNumber, []).DirectoryFor(source));
        Assert.Equal(@"C:\photos", new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []).DirectoryFor(source));
    }

    [Fact]
    public void Folders_get_collision_free_names()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "report (pages)"));
        var writer = new OutputWriter(string.Empty, CollisionBehavior.AddNumber, []);

        string folder = writer.CreateFolder(_directory, "report (pages)");

        Assert.Equal(Path.Combine(_directory, "report (pages) (1)"), folder);
        Assert.True(Directory.Exists(folder));
    }
}
