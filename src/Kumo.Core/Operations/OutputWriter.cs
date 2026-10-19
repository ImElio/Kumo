using Kumo.Core.Files;
using Kumo.Core.Settings;

namespace Kumo.Core.Operations;

public interface IOutputWriter
{
    string DirectoryFor(InputFile source);

    /// <summary>
    /// Produces a file named <paramref name="fileName"/> (or a collision-free variant) in <paramref name="directory"/>.
    /// The content is written to a hidden temporary file first and only moved into place once complete, so a
    /// failure or cancellation never leaves a partial result behind.
    /// </summary>
    Task<string> WriteAsync(string directory, string fileName, long estimatedBytes, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken);

    /// <summary>Creates a new, empty folder with a collision-free name.</summary>
    string CreateFolder(string parent, string name);
}

public static class OutputNaming
{
    public const int MaxAttempts = 10_000;

    public static string Candidate(string fileName, int attempt)
    {
        if (attempt == 0)
            return fileName;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        return $"{stem} ({attempt}){extension}";
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "output" : cleaned;
    }
}

public sealed class OutputWriter : IOutputWriter
{
    private const long SpaceMargin = 16L * 1024 * 1024;

    private readonly string _fixedDirectory;
    private readonly CollisionBehavior _collisions;
    private readonly HashSet<string> _protected;
    private readonly Func<string, long?> _freeSpace;
    private readonly object _gate = new();

    /// <param name="outputDirectory">Empty to write beside each source.</param>
    /// <param name="sources">Paths that must never be replaced, normally the files being processed.</param>
    public OutputWriter(string outputDirectory, CollisionBehavior collisions, IEnumerable<string> sources, Func<string, long?>? freeSpace = null)
    {
        _fixedDirectory = outputDirectory;
        _collisions = collisions;
        _protected = new HashSet<string>(sources.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        _freeSpace = freeSpace ?? AvailableSpace;
    }

    public string DirectoryFor(InputFile source) =>
        _fixedDirectory.Length > 0 ? _fixedDirectory : source.Directory ?? throw new OperationFailedException(OperationErrorKind.InvalidInput, "The file has no folder.");

    public async Task<string> WriteAsync(string directory, string fileName, long estimatedBytes, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        EnsureSpace(directory, estimatedBytes);

        string temporary = Path.Combine(directory, $".kumo-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                File.SetAttributes(temporary, FileAttributes.Hidden);
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.SetAttributes(temporary, FileAttributes.Normal);
            return Commit(temporary, directory, OutputNaming.Sanitize(fileName));
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public string CreateFolder(string parent, string name)
    {
        Directory.CreateDirectory(parent);
        string safe = OutputNaming.Sanitize(name);
        for (int attempt = 0; attempt < OutputNaming.MaxAttempts; attempt++)
        {
            string candidate = Path.Combine(parent, attempt == 0 ? safe : $"{safe} ({attempt})");
            if (Directory.Exists(candidate) || File.Exists(candidate))
                continue;
            Directory.CreateDirectory(candidate);
            lock (_gate)
                _protected.Add(Path.GetFullPath(candidate));
            return candidate;
        }
        throw new OperationFailedException(OperationErrorKind.Unexpected, "No free folder name was found.");
    }

    private string Commit(string temporary, string directory, string fileName)
    {
        lock (_gate)
        {
            string preferred = Path.GetFullPath(Path.Combine(directory, fileName));
            if (_collisions == CollisionBehavior.ReplaceOutput && !_protected.Contains(preferred))
            {
                File.Move(temporary, preferred, overwrite: true);
                _protected.Add(preferred);
                return preferred;
            }

            for (int attempt = 0; attempt < OutputNaming.MaxAttempts; attempt++)
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, OutputNaming.Candidate(fileName, attempt)));
                if (_protected.Contains(candidate) || File.Exists(candidate) || Directory.Exists(candidate))
                    continue;
                try
                {
                    File.Move(temporary, candidate, overwrite: false);
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    continue;
                }
                _protected.Add(candidate);
                return candidate;
            }
        }
        throw new OperationFailedException(OperationErrorKind.Unexpected, "No free file name was found.");
    }

    private void EnsureSpace(string directory, long estimatedBytes)
    {
        long? available = _freeSpace(directory);
        if (available is { } free && free < Math.Max(0, estimatedBytes) + SpaceMargin)
            throw new OperationFailedException(OperationErrorKind.DiskFull, "There isn't enough free disk space.");
    }

    private static long? AvailableSpace(string directory)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            return string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover file is hidden and carries a .tmp name, so it is never mistaken for a result.
        }
    }
}
