using Kumo.Core.Updates;
using static Kumo.Core.Tests.Updates.UpdateFixtures;

namespace Kumo.Core.Tests.Updates;

public class ReleaseSelectorTests
{
    private static readonly SemVersion Current = SemVersion.Parse("0.1.2");

    private static UpdateRelease? Select(UpdateChannel channel, params UpdateRelease[] releases) =>
        ReleaseSelector.SelectUpdate(releases, ReleasePlatform.Windows, Current, channel, InstallationKind.Portable, Architecture.X64);

    [Fact]
    public void Release_channel_ignores_prereleases()
    {
        var update = Select(UpdateChannel.Release, Release("0.2.0-beta.1"), Release("0.2.0-rc.1"), Release("0.1.3"));
        Assert.Equal("0.1.3", update!.Version.ToString());
    }

    [Fact]
    public void Beta_channel_takes_the_highest_version_not_the_newest_publication()
    {
        var update = Select(UpdateChannel.Beta, Release("0.2.0-rc.1"), Release("0.2.0-beta.9"), Release("0.1.3"));
        Assert.Equal("0.2.0-rc.1", update!.Version.ToString());
    }

    [Fact]
    public void Beta_channel_still_receives_stable_releases()
    {
        var update = Select(UpdateChannel.Beta, Release("0.2.0-beta.4"), Release("0.2.0"));
        Assert.Equal("0.2.0", update!.Version.ToString());
    }

    [Theory]
    [InlineData("0.2.0-alpha.1")]
    [InlineData("0.2.0-dev.17")]
    [InlineData("0.2.0-nightly.3")]
    public void Development_and_alpha_builds_stay_off_the_release_and_beta_channels(string version)
    {
        Assert.Null(Select(UpdateChannel.Beta, Release(version)));
        Assert.Null(Select(UpdateChannel.Release, Release(version)));
    }

    [Fact]
    public void Drafts_are_excluded()
    {
        Assert.Null(Select(UpdateChannel.Beta, Release("0.3.0", draft: true)));
    }

    [Fact]
    public void Releases_whose_prerelease_flag_disagrees_with_the_version_are_ignored()
    {
        Assert.Null(Select(UpdateChannel.Release, Release("0.3.0", prerelease: true)));
        Assert.Null(Select(UpdateChannel.Beta, Release("0.3.0-beta.1", prerelease: false)));
    }

    [Fact]
    public void Older_and_equal_versions_are_never_offered()
    {
        Assert.Null(Select(UpdateChannel.Release, Release("0.1.2"), Release("0.1.1"), Release("0.0.9")));
    }

    [Fact]
    public void A_beta_user_on_a_prerelease_is_not_downgraded_to_an_older_stable()
    {
        var current = SemVersion.Parse("0.3.0-beta.2");
        var update = ReleaseSelector.SelectUpdate([Release("0.2.5"), Release("0.3.0-beta.1")], ReleasePlatform.Windows, current, UpdateChannel.Release, InstallationKind.Portable, Architecture.X64);
        Assert.Null(update);
    }

    [Fact]
    public void Releases_without_an_asset_for_this_installation_are_skipped()
    {
        var update = Select(UpdateChannel.Release, Release("0.3.0", portable: false), Release("0.2.0"));
        Assert.Equal("0.2.0", update!.Version.ToString());
    }

    [Fact]
    public void Releases_without_an_asset_for_this_architecture_are_skipped()
    {
        var update = ReleaseSelector.SelectUpdate([Release("0.3.0", architecture: Architecture.X64)], ReleasePlatform.Windows, Current, UpdateChannel.Release, InstallationKind.Portable, Architecture.Arm64);
        Assert.Null(update);
    }

    [Fact]
    public void Installer_installations_need_the_installer_asset()
    {
        var update = ReleaseSelector.SelectUpdate([Release("0.3.0", installer: false), Release("0.2.0")], ReleasePlatform.Windows, Current, UpdateChannel.Release, InstallationKind.Installer, Architecture.X64);
        Assert.Equal("0.2.0", update!.Version.ToString());
    }

    [Theory]
    [InlineData(InstallationKind.Store)]
    [InlineData(InstallationKind.SideloadedPackage)]
    public void Packaged_installations_never_get_a_github_asset(InstallationKind kind)
    {
        Assert.Null(ReleaseSelector.SelectUpdate([Release("0.9.0")], ReleasePlatform.Windows, Current, UpdateChannel.Release, kind, Architecture.X64));
    }

    [Theory]
    [InlineData("Kumo-0.2.0-win-x64.zip", true)]
    [InlineData("SHA256SUMS.txt", true)]
    [InlineData("..\\evil.exe", false)]
    [InlineData("../evil.exe", false)]
    [InlineData("C:\\Windows\\evil.exe", false)]
    [InlineData(".hidden", false)]
    [InlineData("", false)]
    public void Asset_names_must_be_plain_file_names(string name, bool expected)
    {
        Assert.Equal(expected, ReleaseAssets.IsSafeFileName(name));
    }
}
