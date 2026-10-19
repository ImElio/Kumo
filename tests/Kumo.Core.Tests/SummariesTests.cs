using Kumo.Core.Files;
using Kumo.Core.Operations;

namespace Kumo.Core.Tests;

public class SummariesTests
{
    private static readonly MenuEntry Entry = new(OperationCategory.Convert, "WEBP", "Convert to WEBP", InputRequirement.None, []);

    private static BatchResult Result(params FileOutcome[] outcomes) => new(Entry, outcomes);

    private static FileOutcome Saved(string name) => new(name, [name + ".out"]);

    private static FileOutcome Failed(string message) => new("x", [], OperationErrorKind.Corrupt, message);

    [Fact]
    public void All_saved()
    {
        var summary = Summaries.Describe(Result(Saved("a"), Saved("b")));
        Assert.Equal(("Saved 2 files", ResultTone.Success), (summary.Headline, summary.Tone));
    }

    [Fact]
    public void Single_failure_uses_its_message_as_the_headline()
    {
        var summary = Summaries.Describe(Result(Failed("photo.png is damaged.")));
        Assert.Equal(("photo.png is damaged.", ResultTone.Failure), (summary.Headline, summary.Tone));
    }

    [Fact]
    public void Partial_batch_reports_both_counts_and_the_first_problem()
    {
        var summary = Summaries.Describe(Result(Saved("a"), Failed("b is locked."), Failed("c is damaged.")));
        Assert.Equal("Saved 1 file, 2 failed", summary.Headline);
        Assert.Equal("b is locked.", summary.Detail);
        Assert.Equal(ResultTone.Partial, summary.Tone);
    }

    [Fact]
    public void Nothing_saved_explains_why()
    {
        var summary = Summaries.Describe(Result(new FileOutcome("a", [], Message: "a.png is already within 1920 pixels.")));
        Assert.Equal(ResultTone.Nothing, summary.Tone);
        Assert.Equal("a.png is already within 1920 pixels.", summary.Detail);
    }

    [Fact]
    public void Cancellation_mentions_what_was_already_saved()
    {
        var summary = Summaries.Describe(Result(Saved("a"), new FileOutcome("b", [], OperationErrorKind.Cancelled, "Cancelled.")));
        Assert.Equal(("Cancelled after saving 1 file", ResultTone.Cancelled), (summary.Headline, summary.Tone));
    }

    [Fact]
    public void Selection_names_the_kind_of_files()
    {
        Assert.Equal("1 image", Summaries.Selection([new InputFile("a", FileKind.Png, 1)]));
        Assert.Equal("2 PDFs", Summaries.Selection([new InputFile("a", FileKind.Pdf, 1), new InputFile("b", FileKind.Pdf, 1)]));
        Assert.Equal("2 files", Summaries.Selection([new InputFile("a", FileKind.Pdf, 1), new InputFile("b", FileKind.Jpeg, 1)]));
    }

    [Fact]
    public void Progress_counts_the_file_being_worked_on()
    {
        Assert.Equal("Converting 2 of 5…", Summaries.Progress(Entry, new BatchProgress(1, 5, "b", 0.2)));
        Assert.Equal("Converting…", Summaries.Progress(Entry, new BatchProgress(0, 1, "a", 0)));
        Assert.Equal("Converting 5 of 5…", Summaries.Progress(Entry, new BatchProgress(5, 5, "e", 1)));
    }

    [Fact]
    public void Unavailable_explains_folders_unknown_types_and_mixed_selections()
    {
        Assert.Equal("Kumo works with files, not folders.", Summaries.Unavailable([], includedFolders: true));
        Assert.Equal("Kumo doesn't support DOCX files yet.", Summaries.Unavailable([new InputFile("report.docx", FileKind.Unknown, 1)], false));
        Assert.Equal("These files have no action in common. Try them separately.",
            Summaries.Unavailable([new InputFile("a.tif", FileKind.Tiff, 1), new InputFile("b.pdf", FileKind.Pdf, 1)], false));
        Assert.Equal("There's nothing Kumo can do with TIFF files yet.", Summaries.Unavailable([new InputFile("a.tif", FileKind.Tiff, 1)], false));
    }
}
