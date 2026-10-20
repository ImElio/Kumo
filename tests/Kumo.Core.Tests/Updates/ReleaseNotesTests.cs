using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

public class ReleaseNotesTests
{
    private const string GitHubGenerated = """
        ## What's Changed
        * feat: crop images by hand by @ImElio in https://github.com/ImElio/Kumo/pull/12
        * fix: keep **Fluent** styling in `Settings` by @ImElio in https://github.com/ImElio/Kumo/pull/13

        ## New Contributors
        * @someone made their first contribution

        **Full Changelog**: https://github.com/ImElio/Kumo/compare/v0.1.0...v0.2.0
        """;

    [Fact]
    public void Generated_release_notes_become_headings_lists_and_short_links()
    {
        var blocks = ReleaseNotes.Parse(GitHubGenerated);

        Assert.Equal(
            [NotesBlockKind.Heading, NotesBlockKind.Bullet, NotesBlockKind.Bullet, NotesBlockKind.Heading, NotesBlockKind.Bullet, NotesBlockKind.Paragraph],
            blocks.Select(b => b.Kind));
        Assert.Equal("What's Changed", blocks[0].Inlines.Single().Text);
        Assert.Equal(2, blocks[0].Level);

        var pull = blocks[1].Inlines.Last();
        Assert.Equal(NotesInlineKind.Link, pull.Kind);
        Assert.Equal("#12", pull.Text);
        Assert.Equal("https://github.com/ImElio/Kumo/pull/12", pull.Url!.AbsoluteUri);

        Assert.Contains(blocks[2].Inlines, i => i.Kind == NotesInlineKind.Bold && i.Text == "Fluent");
        Assert.Contains(blocks[2].Inlines, i => i.Kind == NotesInlineKind.Code && i.Text == "Settings");

        var compare = blocks[5].Inlines.Last();
        Assert.Equal("v0.1.0...v0.2.0", compare.Text);
        Assert.Equal(NotesInlineKind.Bold, blocks[5].Inlines[0].Kind);
    }

    [Fact]
    public void Lines_of_a_paragraph_are_joined()
    {
        var blocks = ReleaseNotes.Parse("First line\nsecond line\n\nNext paragraph");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("First line second line", blocks[0].Inlines.Single().Text);
    }

    [Fact]
    public void Numbered_lists_keep_their_numbers()
    {
        var blocks = ReleaseNotes.Parse("1. Install\n2) Run");

        Assert.All(blocks, b => Assert.Equal(NotesBlockKind.Numbered, b.Kind));
        Assert.Equal([1, 2], blocks.Select(b => b.Level));
    }

    [Theory]
    [InlineData("[site](http://example.com)")]
    [InlineData("[run](javascript:alert(1))")]
    [InlineData("[file](file:///C:/Windows)")]
    public void Only_https_links_are_kept(string markdown)
    {
        var inlines = ReleaseNotes.Parse(markdown).Single().Inlines;

        Assert.All(inlines, inline => Assert.Equal(NotesInlineKind.Text, inline.Kind));
        Assert.All(inlines, inline => Assert.Null(inline.Url));
    }

    [Fact]
    public void Labelled_https_links_are_kept()
    {
        var inline = ReleaseNotes.ParseInlines("[docs](https://github.com/ImElio/Kumo#readme)").Single();

        Assert.Equal(NotesInlineKind.Link, inline.Kind);
        Assert.Equal("docs", inline.Text);
    }

    [Fact]
    public void Code_fences_are_shown_verbatim()
    {
        var blocks = ReleaseNotes.Parse("```\n# not a heading\n```");

        var line = Assert.Single(blocks);
        Assert.Equal(NotesInlineKind.Code, line.Inlines.Single().Kind);
        Assert.Equal("# not a heading", line.Inlines.Single().Text);
    }

    [Fact]
    public void Empty_or_huge_notes_are_handled()
    {
        Assert.Empty(ReleaseNotes.Parse(null));
        Assert.Empty(ReleaseNotes.Parse("   \n "));
        var huge = ReleaseNotes.Parse(new string('a', ReleaseNotes.MaxLength * 2));
        Assert.True(huge.Single().Inlines.Single().Text.Length <= ReleaseNotes.MaxLength + 1);
    }
}
