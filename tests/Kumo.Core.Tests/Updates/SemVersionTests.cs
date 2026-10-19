using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

public class SemVersionTests
{
    [Theory]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("0.1.10", "0.1.9")]
    [InlineData("1.100.0", "1.99.0")]
    [InlineData("0.2.0", "0.2.0-beta.5")]
    [InlineData("0.2.0-beta.2", "0.2.0-beta.1")]
    [InlineData("0.2.0-rc.1", "0.2.0-beta.9")]
    [InlineData("0.2.0-beta.10", "0.2.0-beta.9")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("2.0.0", "1.99.99")]
    public void Precedence_follows_semver(string higher, string lower)
    {
        Assert.True(SemVersion.Parse(higher) > SemVersion.Parse(lower));
        Assert.True(SemVersion.Parse(lower) < SemVersion.Parse(higher));
    }

    [Fact]
    public void Build_metadata_does_not_change_precedence()
    {
        var a = SemVersion.Parse("1.2.3+abc123");
        var b = SemVersion.Parse("1.2.3+def456");
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal("abc123", a.Build);
        Assert.Equal("1.2.3", a.ToShortString());
        Assert.Equal("1.2.3+abc123", a.ToString());
    }

    [Fact]
    public void Full_semver_example_order_holds()
    {
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"];
        var parsed = ordered.Select(SemVersion.Parse).ToList();
        Assert.Equal(ordered, parsed.OrderBy(v => v).Select(v => v.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-be$ta")]
    [InlineData("v1.2.3")]
    [InlineData(" 1.2.3")]
    public void Malformed_versions_are_rejected(string text)
    {
        Assert.False(SemVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("v0.1.0", "0.1.0")]
    [InlineData("v0.2.0-beta.1", "0.2.0-beta.1")]
    public void Tags_need_a_leading_v(string tag, string expected)
    {
        Assert.True(SemVersion.TryParseTag(tag, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("release-0.1.0")]
    [InlineData("v")]
    [InlineData("vnext")]
    public void Malformed_tags_are_rejected(string tag)
    {
        Assert.False(SemVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void Stage_is_the_first_prerelease_identifier()
    {
        Assert.Equal("rc", SemVersion.Parse("1.0.0-rc.2").Stage);
        Assert.Equal(string.Empty, SemVersion.Parse("1.0.0").Stage);
    }
}
