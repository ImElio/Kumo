using Kumo.Core.Files;

namespace Kumo.Core.Operations;

/// <summary>
/// One choice offered to the user. Several operations can share an entry, for example "Convert → PNG" is served
/// by the image converter for pictures and by the page renderer for PDFs; each file is routed to the first
/// operation that supports it.
/// </summary>
public sealed record MenuEntry(OperationCategory Category, string Label, string Description, InputRequirement Requirement, IReadOnlyList<IFileOperation> Operations)
{
    public bool IsCombined => Operations.Count == 1 && Operations[0].IsCombined;

    public IFileOperation? OperationFor(InputFile file) => Operations.FirstOrDefault(o => o.Supports(file));
}

public interface IFileOperationRegistry
{
    IReadOnlyList<IFileOperation> Operations { get; }

    /// <summary>Entries valid for the whole selection, grouped and ordered for the menu.</summary>
    IReadOnlyList<MenuEntry> EntriesFor(IReadOnlyList<InputFile> files);
}

public sealed class OperationRegistry : IFileOperationRegistry
{
    private readonly List<IFileOperation> _operations;

    public OperationRegistry(IEnumerable<IFileOperation> operations)
    {
        _operations = operations.ToList();
        var duplicate = _operations.GroupBy(o => o.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"Operation id '{duplicate.Key}' is registered twice.", nameof(operations));
    }

    public IReadOnlyList<IFileOperation> Operations => _operations;

    public IReadOnlyList<MenuEntry> EntriesFor(IReadOnlyList<InputFile> files)
    {
        if (files.Count == 0 || files.Any(f => f.Kind == FileKind.Unknown))
            return [];

        var entries = new List<MenuEntry>();
        foreach (var combined in _operations.Where(o => o.IsCombined && o.SupportsSelection(files)))
            entries.Add(new MenuEntry(combined.Category, combined.Label, combined.Description, combined.Requirement, [combined]));

        var perFile = _operations.Where(o => !o.IsCombined)
            .GroupBy(o => (o.Category, o.Label))
            .Select(group => group.ToList());
        foreach (var group in perFile)
        {
            bool everyFileServed = files.All(file => group.Any(o => o.Supports(file)));
            if (!everyFileServed)
                continue;
            var first = group[0];
            var used = group.Where(o => files.Any(o.Supports)).ToList();
            entries.Add(new MenuEntry(first.Category, first.Label, first.Description, first.Requirement, used));
        }

        var order = _operations.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i);
        return entries
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Operations.Min(o => order[o]))
            .ToList();
    }
}
