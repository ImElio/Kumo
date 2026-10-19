using System.Globalization;

namespace Kumo.Core.Operations;

/// <summary>Parses page selections such as "1-3, 5, 8-" (1-based, inclusive; an open end means "to the last page").</summary>
public static class PageRange
{
    public static bool TryParse(string? text, int pageCount, out IReadOnlyList<int> pages, out string error)
    {
        pages = [];
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter the pages to extract, for example 1-3, 5.";
            return false;
        }

        var selected = new List<int>();
        var seen = new HashSet<int>();
        foreach (string raw in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dash = raw.IndexOf('-');
            int first, last;
            if (dash < 0)
            {
                if (!TryPage(raw, out first))
                    return Fail($"\"{raw}\" is not a page number.", out error);
                last = first;
            }
            else
            {
                string start = raw[..dash].Trim();
                string end = raw[(dash + 1)..].Trim();
                if (!TryPage(start.Length == 0 ? "1" : start, out first))
                    return Fail($"\"{raw}\" is not a valid range.", out error);
                if (end.Length == 0)
                    last = pageCount;
                else if (!TryPage(end, out last))
                    return Fail($"\"{raw}\" is not a valid range.", out error);
            }

            if (first > last)
                return Fail($"\"{raw}\" runs backwards.", out error);
            if (last > pageCount)
                return Fail(pageCount == 1 ? "The document has only 1 page." : $"The document has only {pageCount} pages.", out error);

            for (int page = first; page <= last; page++)
            {
                if (seen.Add(page))
                    selected.Add(page);
            }
        }

        if (selected.Count == 0)
            return Fail("No pages were selected.", out error);
        pages = selected;
        return true;
    }

    /// <summary>Checks the syntax only, before the document's page count is known.</summary>
    public static bool IsWellFormed(string? text, out string error) => TryParseBounded(text, int.MaxValue, out error);

    private static bool TryParseBounded(string? text, int pageCount, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter the pages to extract, for example 1-3, 5.";
            return false;
        }
        foreach (string raw in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dash = raw.IndexOf('-');
            string start = dash < 0 ? raw : raw[..dash].Trim();
            string end = dash < 0 ? raw : raw[(dash + 1)..].Trim();
            if (!TryPage(start.Length == 0 ? "1" : start, out int first))
                return Fail($"\"{raw}\" is not a page number.", out error);
            int last = pageCount;
            if (end.Length > 0 && !TryPage(end, out last))
                return Fail($"\"{raw}\" is not a valid range.", out error);
            if (first > last)
                return Fail($"\"{raw}\" runs backwards.", out error);
        }
        return true;
    }

    private static bool TryPage(string text, out int page) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
