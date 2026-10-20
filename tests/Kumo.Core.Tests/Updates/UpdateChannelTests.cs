using System.Net;
using Kumo.Core.Diagnostics;
using Kumo.Core.Settings;
using Kumo.Core.Updates;
using static Kumo.Core.Tests.Updates.UpdateFixtures;

namespace Kumo.Core.Tests.Updates;

public sealed class UpdateChannelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-channels-").FullName;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    private UpdateChannel _channel = UpdateChannel.Release;
    private bool _automatic = true;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private UpdateService Service(IUpdateSource source, string current = "0.1.0", InstallationKind kind = InstallationKind.Portable) =>
        new(new UpdateEnvironment(ReleasePlatform.Windows, SemVersion.Parse(current), kind, Architecture.X64, Repository, string.Empty), source,
            new UpdateStateStore(Path.Combine(_directory, "updates.json"), Log.Null), () => _channel, Log.Null, _clock, () => _automatic);

    private static readonly UpdateRelease[] Catalogue =
    [
        Release("0.1.1"),
        Release("0.2.0-alpha.1"),
        Release("0.2.0-beta.1"),
        Release("0.2.0-beta.2"),
        Release("0.2.0-rc.1"),
        Release("0.3.0-dev.7"),
    ];

    [Fact]
    public void Release_is_the_default_channel()
    {
        Assert.Equal(UpdateChannel.Release, new KumoSettings().UpdateChannel);
    }

    [Fact]
    public void The_selected_channel_survives_a_restart()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"), Log.Null);
        store.Save(new KumoSettings { UpdateChannel = UpdateChannel.Development });

        Assert.Equal(UpdateChannel.Development, store.Load().UpdateChannel);
        Assert.Contains("\"UpdateChannel\": \"Development\"", File.ReadAllText(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public void Settings_written_before_the_channel_rename_still_load()
    {
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{ "Theme": "Dark", "UpdateChannel": "Stable" }""");

        var settings = new SettingsStore(path, Log.Null).Load();

        Assert.Equal(UpdateChannel.Release, settings.UpdateChannel);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(UpdateChannel.Release, "0.1.1")]
    [InlineData(UpdateChannel.Beta, "0.2.0-rc.1")]
    [InlineData(UpdateChannel.Development, "0.3.0-dev.7")]
    public void Each_channel_offers_the_highest_version_it_accepts(UpdateChannel channel, string expected)
    {
        var update = ReleaseSelector.SelectUpdate(Catalogue, ReleasePlatform.Windows, SemVersion.Parse("0.1.0"), channel, InstallationKind.Portable, Architecture.X64);
        Assert.Equal(expected, update!.Version.ToString());
    }

    [Theory]
    [InlineData("0.2.0-alpha.1", false, false, true)]
    [InlineData("0.2.0-beta.1", false, true, true)]
    [InlineData("0.2.0-rc.1", false, true, true)]
    [InlineData("0.2.0-dev.3", false, false, true)]
    [InlineData("0.2.0-nightly.3", false, false, false)]
    [InlineData("0.2.0", true, true, true)]
    public void Channel_eligibility(string version, bool release, bool beta, bool development)
    {
        var candidate = Release(version);
        Assert.Equal(release, ReleaseSelector.IsEligible(candidate, UpdateChannel.Release));
        Assert.Equal(beta, ReleaseSelector.IsEligible(candidate, UpdateChannel.Beta));
        Assert.Equal(development, ReleaseSelector.IsEligible(candidate, UpdateChannel.Development));
    }

    [Fact]
    public async Task Switching_from_release_to_beta_finds_a_newer_prerelease()
    {
        var source = new FakeSource { Releases = [Release("0.1.0"), Release("0.2.0-beta.1")] };
        var service = Service(source);

        var onRelease = await service.CheckAsync(manual: false, CancellationToken.None);
        _channel = UpdateChannel.Beta;
        var onBeta = await service.ChannelChangedAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, onRelease.Status);
        Assert.Equal(UpdateStatus.Available, onBeta.Status);
        Assert.Equal("Kumo 0.2.0-beta.1 is available.", onBeta.Message);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Returning_to_release_from_a_beta_never_downgrades()
    {
        var source = new FakeSource { Releases = [Release("0.2.5"), Release("0.3.0-beta.1"), Release("0.3.0-beta.2")] };
        _channel = UpdateChannel.Release;
        var service = Service(source, current: "0.3.0-beta.2");

        var result = await service.ChannelChangedAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Release);
        Assert.Equal("You're using the pre-release 0.3.0-beta.2. Kumo will update when a newer stable release is available.", result.Message);
    }

    [Fact]
    public async Task Returning_to_release_offers_the_next_higher_stable_release()
    {
        var source = new FakeSource { Releases = [Release("0.3.0"), Release("0.3.1-beta.1")] };
        var service = Service(source, current: "0.3.0-beta.2");

        var result = await service.ChannelChangedAsync(CancellationToken.None);

        Assert.Equal("0.3.0", result.Release!.Version.ToString());
    }

    [Fact]
    public async Task Development_channel_without_published_development_builds_falls_back_to_other_prereleases()
    {
        _channel = UpdateChannel.Development;
        var service = Service(new FakeSource { Releases = [Release("0.2.0-alpha.2"), Release("0.1.1")] });

        var result = await service.ChannelChangedAsync(CancellationToken.None);

        Assert.Equal("0.2.0-alpha.2", result.Release!.Version.ToString());
    }

    [Fact]
    public async Task Disabled_automatic_checks_make_no_requests()
    {
        _automatic = false;
        var source = new FakeSource { Releases = [Release("0.2.0")] };

        var result = await Service(source).CheckAsync(manual: false, CancellationToken.None);

        Assert.Equal(UpdateStatus.Skipped, result.Status);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Manual_checks_work_with_automatic_checks_disabled()
    {
        _automatic = false;
        var source = new FakeSource { Releases = [Release("0.2.0")] };

        var result = await Service(source).CheckAsync(manual: true, CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task A_channel_change_while_checking_waits_and_checks_again_for_the_new_channel()
    {
        var source = new FakeSource { Releases = [Release("0.2.0-beta.1")], Gate = new TaskCompletionSource() };
        var service = Service(source);

        var releaseCheck = service.CheckAsync(manual: true, CancellationToken.None);
        var duplicate = service.CheckAsync(manual: true, CancellationToken.None);
        _channel = UpdateChannel.Beta;
        var betaCheck = service.ChannelChangedAsync(CancellationToken.None);
        source.Gate.SetResult();

        Assert.Same(releaseCheck, duplicate);
        Assert.Equal(UpdateStatus.UpToDate, (await releaseCheck).Status);
        Assert.Equal(UpdateStatus.Available, (await betaCheck).Status);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Status_moves_from_idle_through_checking_to_the_result()
    {
        var source = new FakeSource { Releases = [Release("0.2.0")], Gate = new TaskCompletionSource() };
        var service = Service(source);
        var seen = new List<UpdateStatus>();
        service.StatusChanged += status => seen.Add(status.Status);

        Assert.Equal(UpdateStatus.Idle, service.Status.Status);
        var check = service.CheckAsync(manual: true, CancellationToken.None);
        Assert.Equal(UpdateStatus.Checking, service.Status.Status);
        source.Gate.SetResult();
        await check;

        Assert.Equal([UpdateStatus.Checking, UpdateStatus.Available], seen);
        Assert.Equal(UpdateStatus.Available, service.Status.Status);
    }

    [Fact]
    public async Task Skipped_automatic_checks_keep_the_previous_status()
    {
        var service = Service(new FakeSource { Releases = [Release("0.2.0")] });
        await service.CheckAsync(manual: true, CancellationToken.None);
        var seen = new List<UpdateStatus>();
        service.StatusChanged += status => seen.Add(status.Status);

        await service.CheckAsync(manual: false, CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, service.Status.Status);
        Assert.DoesNotContain(UpdateStatus.Checking, seen);
    }

    [Fact]
    public async Task Network_failures_are_reported_without_technical_detail()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("No such host is known. (api.github.com:443)"));
        var source = new GitHubReleaseSource(new HttpClient(handler), Repository, "0.1.0");

        var result = await Service(source).CheckAsync(manual: true, CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal("Couldn't check for updates. Check your connection and try again.", result.Message);
        Assert.DoesNotContain("api.github.com", result.Message);
    }

    [Fact]
    public async Task Server_errors_ask_to_try_later()
    {
        var source = new GitHubReleaseSource(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))), Repository, "0.1.0");

        var result = await Service(source).CheckAsync(manual: true, CancellationToken.None);

        Assert.Equal("Couldn't check for updates. Try again later.", result.Message);
    }

    [Theory]
    [InlineData(UpdateChannel.Release)]
    [InlineData(UpdateChannel.Beta)]
    [InlineData(UpdateChannel.Development)]
    public async Task Store_installations_ignore_every_channel(UpdateChannel channel)
    {
        _channel = channel;
        var source = new FakeSource { Releases = [Release("9.0.0"), Release("9.1.0-beta.1")] };
        var service = Service(source, kind: InstallationKind.Store);

        var result = await service.ChannelChangedAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Disabled, result.Status);
        Assert.Equal("Updates managed by Microsoft Store.", result.Message);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Selecting_a_channel_only_reports_and_never_downloads()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host == "api.github.com"
            ? JsonResponse(new[] { Json("v0.2.0-beta.1", prerelease: true, assets: ["Kumo-0.2.0-beta.1-win-x64.zip", "Kumo-0.2.0-beta.1-win-x64-setup.exe", "SHA256SUMS.txt"]) })
            : throw new InvalidOperationException("Nothing but the release list may be requested."));
        _channel = UpdateChannel.Beta;
        var service = Service(new GitHubReleaseSource(new HttpClient(handler), Repository, "0.1.0"), kind: InstallationKind.Installer);

        var result = await service.ChannelChangedAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.All(handler.Requests, uri => Assert.Equal("api.github.com", uri.Host));
    }
}
