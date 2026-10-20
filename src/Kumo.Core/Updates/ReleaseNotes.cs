using System.Text;
using System.Text.RegularExpressions;

namespace Kumo.Core.Updates;

public enum NotesInlineKind
{
    Text,
    Bold,
    Code,
    Link,
}

/// <param name="Url">Set only for links; always an absolute https URL.</param>
public sealed record NotesInline(NotesInlineKind Kind, string Text, Uri? Url = null);

public enum NotesBlockKind
{
    Heading,
    Paragraph,
    Bullet,
    Numbered,

    /// <summary>A screenshot on its own line; <see cref="NotesBlock.Image"/> holds its address, the inline its alt text.</summary>
    Image,
}

/// <param name="Level">Heading level (1–3), or the item number for numbered lists.</param>
/// <param name="Image">Set only for image blocks; always an https address on a GitHub image host.</param>
public sealed record NotesBlock(NotesBlockKind Kind, IReadOnlyList<NotesInline> Inlines, int Level = 0, Uri? Image = null);

/// <summary>
/// Reads the Markdown that GitHub uses for release notes (headings, lists, bold, inline code, links and the bare
/// URLs of generated notes) into simple blocks for display. Anything else is shown as plain text; nothing is executed
/// or fetched, only https links survive, and images are kept only when GitHub itself hosts them.
/// </summary>
public static partial class ReleaseNotes
{
    public const int MaxLength = 20_000;

    public static IReadOnlyList<NotesBlock> Parse(string? markdown)
    {
        var blocks = new List<NotesBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
            return blocks;

        string text = markdown.Length > MaxLength ? markdown[..MaxLength] + "…" : markdown;
        var paragraph = new StringBuilder();
        bool inFence = false;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;
            blocks.Add(new NotesBlock(NotesBlockKind.Paragraph, ParseInlines(paragraph.ToString())));
            paragraph.Clear();
        }

        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                blocks.Add(new NotesBlock(NotesBlockKind.Paragraph, [new NotesInline(NotesInlineKind.Code, line)]));
                continue;
            }

            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || HorizontalRule().IsMatch(trimmed))
            {
                FlushParagraph();
                continue;
            }
            if (ImagesOnly(trimmed) is { Count: > 0 } images)
            {
                FlushParagraph();
                blocks.AddRange(images);
                continue;
            }
            if (Heading().Match(trimmed) is { Success: true } heading)
            {
                FlushParagraph();
                blocks.Add(new NotesBlock(NotesBlockKind.Heading, ParseInlines(heading.Groups[2].Value), Math.Min(3, heading.Groups[1].Length)));
                continue;
            }
            if (Bullet().Match(trimmed) is { Success: true } bullet)
            {
                FlushParagraph();
                blocks.Add(new NotesBlock(NotesBlockKind.Bullet, ParseInlines(bullet.Groups[1].Value)));
                continue;
            }
            if (Numbered().Match(trimmed) is { Success: true } numbered)
            {
                FlushParagraph();
                int number = int.TryParse(numbered.Groups[1].Value, out int n) ? n : blocks.Count(b => b.Kind == NotesBlockKind.Numbered) + 1;
                blocks.Add(new NotesBlock(NotesBlockKind.Numbered, ParseInlines(numbered.Groups[2].Value), number));
                continue;
            }

            if (paragraph.Length > 0)
                paragraph.Append(' ');
            paragraph.Append(trimmed);
        }
        FlushParagraph();
        return blocks;
    }

    public static IReadOnlyList<NotesInline> ParseInlines(string text)
    {
        var inlines = new List<NotesInline>();
        int position = 0;
        foreach (Match match in InlineToken().Matches(text))
        {
            if (match.Index > position)
                AddText(inlines, text[position..match.Index]);

            if (match.Groups["code"].Success)
                inlines.Add(new NotesInline(NotesInlineKind.Code, match.Groups["code"].Value));
            else if (match.Groups["bold"].Success)
                inlines.Add(new NotesInline(NotesInlineKind.Bold, match.Groups["bold"].Value));
            else if (match.Groups["label"].Success)
                AddLink(inlines, match.Groups["label"].Value, match.Groups["href"].Value);
            else
                AddLink(inlines, ShortenUrl(match.Groups["url"].Value), match.Groups["url"].Value);
            position = match.Index + match.Length;
        }
        if (position < text.Length)
            AddText(inlines, text[position..]);
        return inlines;
    }

    public static bool IsAllowedImage(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && string.IsNullOrEmpty(url.UserInfo)
        && (ImageHosts.Contains(url.Host)
            || (url.Host == "github.com" && url.AbsolutePath.StartsWith("/user-attachments/assets/", StringComparison.Ordinal)));

    private static readonly HashSet<string> ImageHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "user-images.githubusercontent.com",
        "private-user-images.githubusercontent.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
    };

    /// <summary>
    /// The images of a line that holds nothing but images: Markdown images, the &lt;img&gt; tags GitHub inserts for
    /// pasted screenshots, or a bare attachment URL. Images from other hosts are dropped; their alt text is kept.
    /// </summary>
    private static List<NotesBlock> ImagesOnly(string line)
    {
        var images = new List<NotesBlock>();
        if (BareAttachment().IsMatch(line))
        {
            AddImage(images, line, string.Empty);
            return images;
        }
        var matches = ImageToken().Matches(line);
        if (matches.Count == 0 || ImageToken().Replace(line, string.Empty).Trim().Length > 0)
            return images;
        foreach (Match match in matches)
        {
            string source = match.Groups["src"].Success ? match.Groups["src"].Value : HtmlAttribute(match.Value, "src");
            string alt = match.Groups["alt"].Success ? match.Groups["alt"].Value : HtmlAttribute(match.Value, "alt");
            AddImage(images, source, alt);
        }
        return images;
    }

    private static void AddImage(List<NotesBlock> images, string source, string alt)
    {
        var caption = alt.Length > 0 ? new List<NotesInline> { new(NotesInlineKind.Text, alt) } : new List<NotesInline>();
        if (Uri.TryCreate(System.Net.WebUtility.HtmlDecode(source), UriKind.Absolute, out var url) && IsAllowedImage(url))
            images.Add(new NotesBlock(NotesBlockKind.Image, caption, Image: url));
        else if (caption.Count > 0)
            images.Add(new NotesBlock(NotesBlockKind.Paragraph, caption));
    }

    private static string HtmlAttribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.IgnoreCase);
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups["v"].Value) : string.Empty;
    }

    private static void AddLink(List<NotesInline> inlines, string label, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
            inlines.Add(new NotesInline(NotesInlineKind.Link, label, url));
        else
            AddText(inlines, label);
    }

    private static void AddText(List<NotesInline> inlines, string text)
    {
        string cleaned = text.Replace("\\*", "*", StringComparison.Ordinal).Replace("\\_", "_", StringComparison.Ordinal);
        if (cleaned.Length > 0)
            inlines.Add(new NotesInline(NotesInlineKind.Text, cleaned));
    }

    /// <summary>GitHub's generated notes link pull requests and comparisons in full; "#12" and "v0.1.0...v0.2.0" read better.</summary>
    private static string ShortenUrl(string url)
    {
        if (PullRequest().Match(url) is { Success: true } pull)
            return "#" + pull.Groups[1].Value;
        if (Compare().Match(url) is { Success: true } compare)
            return compare.Groups[1].Value;
        return url;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(\d{1,4})[.)]\s+(.+)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^([-*_])(\s*\1){2,}$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|__(?<bold>.+?)__|\[(?<label>[^\]]+)\]\((?<href>[^)\s]+)\)|(?<url>https://[^\s)<>]+)")]
    private static partial Regex InlineToken();

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<src>[^)\s]+)\)|<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageToken();

    [GeneratedRegex(@"^https://github\.com/user-attachments/assets/[0-9a-fA-F-]+$")]
    private static partial Regex BareAttachment();

    [GeneratedRegex(@"^https://github\.com/[^/]+/[^/]+/pull/(\d+)/?$")]
    private static partial Regex PullRequest();

    [GeneratedRegex(@"^https://github\.com/[^/]+/[^/]+/compare/(\S+\.\.\.\S+)$")]
    private static partial Regex Compare();
}
