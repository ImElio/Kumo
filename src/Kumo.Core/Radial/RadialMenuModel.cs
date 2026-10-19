using Kumo.Core.Operations;

namespace Kumo.Core.Radial;

public enum RadialItemKind
{
    Category,
    Action,
}

public sealed record RadialItem(string Label, string Description, RadialItemKind Kind, OperationCategory Category, MenuEntry? Entry)
{
    public bool OpensSubmenu => Kind == RadialItemKind.Category;
}

public enum RadialActivationKind
{
    None,
    OpenedSubmenu,
    Execute,
}

public readonly record struct RadialActivation(RadialActivationKind Kind, MenuEntry? Entry)
{
    public static RadialActivation None => new(RadialActivationKind.None, null);
}

/// <summary>
/// Navigation state of the radial menu, independent of rendering. The root lists categories; a category with a
/// single entry is offered directly so one-step actions such as "Merge" need no extra level.
/// </summary>
public sealed class RadialMenuModel
{
    public const int MaxRootItems = 8;

    private readonly IReadOnlyList<MenuEntry> _entries;
    private readonly List<RadialItem> _root;
    private OperationCategory? _openCategory;

    public RadialMenuModel(IReadOnlyList<MenuEntry> entries)
    {
        _entries = entries;
        _root = entries
            .GroupBy(e => e.Category)
            .OrderBy(g => g.Key)
            .Select(g => g.Count() == 1
                ? new RadialItem(CategoryLabel(g.Key, g.First()), g.First().Description, RadialItemKind.Action, g.Key, g.First())
                : new RadialItem(CategoryLabel(g.Key, null), CategoryDescription(g.Key), RadialItemKind.Category, g.Key, null))
            .Take(MaxRootItems)
            .ToList();
        Items = _root;
    }

    public IReadOnlyList<RadialItem> Items { get; private set; }

    public bool IsEmpty => _root.Count == 0;

    public bool IsAtRoot => _openCategory is null;

    public OperationCategory? OpenCategory => _openCategory;

    public string Title => _openCategory is { } category ? CategoryLabel(category, null) : string.Empty;

    public RadialActivation Activate(int index)
    {
        if (index < 0 || index >= Items.Count)
            return RadialActivation.None;
        var item = Items[index];
        if (item.Kind == RadialItemKind.Action)
            return new RadialActivation(RadialActivationKind.Execute, item.Entry);

        _openCategory = item.Category;
        Items = _entries
            .Where(e => e.Category == item.Category)
            .Select(e => new RadialItem(e.Label, e.Description, RadialItemKind.Action, e.Category, e))
            .ToList();
        return new RadialActivation(RadialActivationKind.OpenedSubmenu, null);
    }

    public bool Back()
    {
        if (_openCategory is null)
            return false;
        _openCategory = null;
        Items = _root;
        return true;
    }

    public int IndexOfCategory(OperationCategory category)
    {
        for (int i = 0; i < _root.Count; i++)
        {
            if (_root[i].Category == category)
                return i;
        }
        return -1;
    }

    private static string CategoryLabel(OperationCategory category, MenuEntry? single) => category switch
    {
        OperationCategory.Convert when single != null => $"To {single.Label}",
        OperationCategory.Merge when single != null => single.Label,
        _ => category.ToString(),
    };

    private static string CategoryDescription(OperationCategory category) => category switch
    {
        OperationCategory.Convert => "Convert to another format",
        OperationCategory.Resize => "Resize images",
        OperationCategory.Compress => "Make files smaller",
        OperationCategory.Rotate => "Rotate images",
        OperationCategory.Merge => "Combine files",
        OperationCategory.Split => "Split into separate files",
        OperationCategory.Extract => "Extract part of a file",
        OperationCategory.Protect => "Protect with a password",
        _ => category.ToString(),
    };
}
