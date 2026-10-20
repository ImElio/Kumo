using Kumo.Core.Updates;
using static Kumo.Core.Tests.Updates.UpdateFixtures;

namespace Kumo.Core.Tests.Updates;

public class ReleasePlatformTests
{
    private static readonly SemVersion Current = SemVersion.Parse("0.2.0");

    private static UpdateRelease? Select(IEnumerable<UpdateRelease> releases, UpdateChannel channel,
        ReleasePlatform platform = ReleasePlatform.Windows, Architecture architecture = Architecture.X64) =>
        ReleaseSelector.SelectUpdate(releases, platform, Current, channel, InstallationKind.Portable, architecture);

    [Theory]
    [InlineData("windows/v1.2.0", ReleasePlatform.Windows, "1.2.0")]
    [InlineData("windows/v1.2.0-rc.1", ReleasePlatform.Windows, "1.2.0-rc.1")]
    [InlineData("linux/v0.1.0-alpha.3", ReleasePlatform.Linux, "0.1.0-alpha.3")]
    [InlineData("v0.1.1", ReleasePlatform.Windows, "0.1.1")]
    public void Tags_carry_their_platform(string tag, ReleasePlatform platform, string version)
    {
        Assert.True(ReleaseTag.TryParse(tag, out var parsedPlatform, out var parsedVersion));
        Assert.Equal(platform, parsedPlatform);
        Assert.Equal(SemVersion.Parse(version), parsedVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("macos/v1.0.0")]
    [InlineData("Windows/v1.0.0")]
    [InlineData("windows/1.0.0")]
    [InlineData("windows/v1.0")]
    [InlineData("linux/")]
    [InlineData("windows/linux/v1.0.0")]
    public void Unknown_or_malformed_tags_are_refused(string? tag)
    {
        Assert.False(ReleaseTag.TryParse(tag, out _, out _));
    }

    [Fact]
    public void Formatted_tags_round_trip()
    {
        var version = SemVersion.Parse("2.0.0-beta.4");
        foreach (var platform in Enum.GetValues<ReleasePlatform>())
        {
            Assert.True(ReleaseTag.TryParse(ReleaseTag.Format(platform, version), out var parsed, out var parsedVersion));
            Assert.Equal(platform, parsed);
            Assert.Equal(version, parsedVersion);
        }
        Assert.Equal("windows/v2.0.0-beta.4", ReleaseTag.Format(ReleasePlatform.Windows, version));
    }

    [Fact]
    public void A_higher_Linux_version_is_never_offered_to_Windows()
    {
        var releases = new[]
        {
            Release("0.3.0"),
            Release("9.0.0", platform: ReleasePlatform.Linux),
        };

        Assert.Equal("0.3.0", Select(releases, UpdateChannel.Development)!.Version.ToString());
    }

    [Fact]
    public void Only_Linux_releases_means_no_Windows_update()
    {
        UpdateRelease[] releases = [Release("1.0.0", platform: ReleasePlatform.Linux), Release("1.1.0-beta.1", platform: ReleasePlatform.Linux)];

        foreach (var channel in Enum.GetValues<UpdateChannel>())
            Assert.Null(Select(releases, channel));
    }

    [Fact]
    public void Linux_selection_ignores_Windows_releases()
    {
        var releases = new[] { Release("5.0.0"), Release("0.3.0", platform: ReleasePlatform.Linux) };

        Assert.Equal(ReleasePlatform.Linux, Select(releases, UpdateChannel.Release, ReleasePlatform.Linux)!.Platform);
    }

    [Theory]
    [InlineData(UpdateChannel.Release, "0.3.0")]
    [InlineData(UpdateChannel.Beta, "0.4.0-rc.1")]
    [InlineData(UpdateChannel.Development, "0.5.0-alpha.1")]
    public void Channels_filter_within_the_platform(UpdateChannel channel, string expected)
    {
        var releases = new[]
        {
            Release("0.3.0"),
            Release("0.4.0-beta.2"),
            Release("0.4.0-rc.1"),
            Release("0.5.0-alpha.1"),
            Release("1.0.0", platform: ReleasePlatform.Linux),
            Release("2.0.0-rc.1", platform: ReleasePlatform.Linux),
            Release("3.0.0-alpha.1", platform: ReleasePlatform.Linux),
        };

        Assert.Equal(expected, Select(releases, channel)!.Version.ToString());
    }

    [Fact]
    public void Alpha_builds_stay_in_Development()
    {
        UpdateRelease[] releases = [Release("0.3.0-alpha.1")];

        Assert.Null(Select(releases, UpdateChannel.Release));
        Assert.Null(Select(releases, UpdateChannel.Beta));
        Assert.NotNull(Select(releases, UpdateChannel.Development));
    }

    [Fact]
    public void Legacy_and_prefixed_Windows_tags_compete_by_SemVer()
    {
        var releases = new[] { Legacy("0.4.0"), Release("0.3.0") };

        Assert.Equal("v0.4.0", Select(releases, UpdateChannel.Release)!.Tag);
    }

    [Fact]
    public void A_lower_version_published_later_is_not_a_downgrade()
    {
        var newer = Release("0.1.9") with { PublishedAt = DateTimeOffset.UnixEpoch.AddYears(60) };

        Assert.Null(Select([newer], UpdateChannel.Development));
    }

    [Fact]
    public void Architecture_is_matched_within_the_platform()
    {
        var releases = new[] { Release("0.3.0", architecture: Architecture.X64), Release("0.2.5", architecture: Architecture.Arm64) };

        Assert.Equal("0.2.5", Select(releases, UpdateChannel.Release, architecture: Architecture.Arm64)!.Version.ToString());
    }

    [Fact]
    public async Task GitHub_listing_reads_the_platform_from_each_tag()
    {
        var handler = new FakeHandler(_ => JsonResponse(new[]
        {
            Json("windows/v0.3.0"),
            Json("linux/v0.3.0"),
            Json("v0.2.0"),
            Json("macos/v0.3.0"),
        }));

        var releases = await new GitHubReleaseSource(new HttpClient(handler), Repository, "0.1.0").GetReleasesAsync(CancellationToken.None);

        Assert.Equal([ReleasePlatform.Windows, ReleasePlatform.Linux, ReleasePlatform.Windows], releases.Select(r => r.Platform));
        Assert.Equal(["windows/v0.3.0", "linux/v0.3.0", "v0.2.0"], releases.Select(r => r.Tag));
    }

    private static UpdateRelease Legacy(string version)
    {
        var release = Release(version);
        return release with { Tag = "v" + version };
    }
}
