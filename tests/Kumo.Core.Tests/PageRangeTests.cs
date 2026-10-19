using Kumo.Core.Operations;

namespace Kumo.Core.Tests;

public class PageRangeTests
{
    [Theory]
    [InlineData("1-3, 5", 10, new[] { 1, 2, 3, 5 })]
    [InlineData("8-", 10, new[] { 8, 9, 10 })]
    [InlineData("-2", 10, new[] { 1, 2 })]
    [InlineData("4; 2 ;4", 10, new[] { 4, 2 })]
    [InlineData(" 7 ", 7, new[] { 7 })]
    public void Valid_selections(string text, int pageCount, int[] expected)
    {
        Assert.True(PageRange.TryParse(text, pageCount, out var pages, out _));
        Assert.Equal(expected, pages);
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("0", 5)]
    [InlineData("6", 5)]
    [InlineData("3-1", 5)]
    [InlineData("a-b", 5)]
    [InlineData("1-9", 5)]
    [InlineData(",,", 5)]
    public void Invalid_selections_explain_the_problem(string text, int pageCount)
    {
        Assert.False(PageRange.TryParse(text, pageCount, out _, out string error));
        Assert.NotEmpty(error);
    }
}
