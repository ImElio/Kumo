using Kumo.Core.Diagnostics;
using Kumo.Core.Settings;
using Kumo.Core.Updates;
using static Kumo.Core.Tests.Updates.UpdateFixtures;

namespace Kumo.Core.Tests.Updates;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-updates-").FullName;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private UpdateStateStore Store => new(Path.Combine(_directory, "updates.json"), Log.Null);

    private static UpdateEnvironment Environment(InstallationKind kind = InstallationKind.Portable, bool repository = true, string publisher = "") =>
        new(ReleasePlatform.Windows, SemVersion.Parse("0.1.2"), kind, Architecture.X64, repository ? Repository : null, publisher);

    private UpdateService Service(FakeSource source, UpdateEnvironment? environment = null, UpdateChannel channel = UpdateChannel.Release) =>
        new(environment ?? Environment(), source, Store, () => channel, Log.Null, _clock);

    [Fact]
    public async Task Finds_an_available_update()
    {
        var source = new FakeSource { Releases = [Release("0.2.0"), Release("0.1.1")] };

        var result = await Service(source).CheckAsync(manual: true, CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("0.2.0", result.Release!.Version.ToString());
        Assert.Equal("Kumo 0.2.0 is available.", result.Message);
    }

    [Fact]
    public async Task Reports_up_to_date()
    {
        var result = await Service(new FakeSource { Releases = [Release("0.1.2")] }).CheckAsync(true, CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Equal("Kumo is up to date.", result.Message);
    }

    [Fact]
    public async Task Channel_setting_is_honoured()
    {
        var source = new FakeSource { Releases = [Release("0.2.0-beta.1")] };

        Assert.Equal(UpdateStatus.UpToDate, (await Service(source).CheckAsync(true, CancellationToken.None)).Status);
        Assert.Equal(UpdateStatus.Available, (await Service(source, channel: UpdateChannel.Beta).CheckAsync(true, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Store_installations_never_contact_github()
    {
        var source = new FakeSource { Releases = [Release("9.0.0")] };
        var service = Service(source, Environment(InstallationKind.Store));

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.Equal(UpdateCapability.StoreManaged, service.Capability);
        Assert.Equal(UpdateStatus.Disabled, result.Status);
        Assert.Equal("Updates managed by Microsoft Store.", result.Message);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Builds_without_a_configured_repository_do_not_check()
    {
        var source = new FakeSource();
        var service = Service(source, Environment(repository: false));

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.Equal(UpdateCapability.Unconfigured, service.Capability);
        Assert.Equal(UpdateStatus.Disabled, result.Status);
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData(InstallationKind.Portable, "", UpdateCapability.NotifyOnly)]
    [InlineData(InstallationKind.SideloadedPackage, "", UpdateCapability.NotifyOnly)]
    [InlineData(InstallationKind.Installer, "", UpdateCapability.NotifyOnly)]
    [InlineData(InstallationKind.Installer, "CN=Kumo", UpdateCapability.InstallVerified)]
    [InlineData(InstallationKind.Portable, "CN=Kumo", UpdateCapability.NotifyOnly)]
    [InlineData(InstallationKind.Store, "CN=Kumo", UpdateCapability.StoreManaged)]
    public void Only_signed_installer_installations_may_install_updates(InstallationKind kind, string publisher, UpdateCapability expected)
    {
        Assert.Equal(expected, Environment(kind, publisher: publisher).Capability);
    }

    [Fact]
    public async Task Automatic_checks_run_at_most_once_a_day()
    {
        var source = new FakeSource { Releases = [Release("0.2.0")] };
        var service = Service(source);

        await service.CheckAsync(manual: false, CancellationToken.None);
        _clock.Now += TimeSpan.FromHours(23);
        var second = await service.CheckAsync(manual: false, CancellationToken.None);
        _clock.Now += TimeSpan.FromHours(2);
        var third = await service.CheckAsync(manual: false, CancellationToken.None);

        Assert.Equal(UpdateStatus.Skipped, second.Status);
        Assert.Equal(UpdateStatus.Available, third.Status);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Manual_checks_ignore_the_daily_limit()
    {
        var source = new FakeSource { Releases = [Release("0.2.0")] };
        var service = Service(source);

        await service.CheckAsync(false, CancellationToken.None);
        await service.CheckAsync(true, CancellationToken.None);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Last_check_time_survives_a_restart()
    {
        var source = new FakeSource { Releases = [Release("0.2.0")] };
        await Service(source).CheckAsync(false, CancellationToken.None);

        var afterRestart = await Service(source).CheckAsync(false, CancellationToken.None);

        Assert.Equal(UpdateStatus.Skipped, afterRestart.Status);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Rate_limits_postpone_automatic_checks_until_reset()
    {
        var source = new FakeSource { Failure = new UpdateException(UpdateErrorKind.RateLimited, "limited", retryAfter: _clock.Now + TimeSpan.FromHours(30)) };
        var service = Service(source);

        var first = await service.CheckAsync(false, CancellationToken.None);
        _clock.Now += TimeSpan.FromHours(25);
        source.Failure = null;
        var second = await service.CheckAsync(false, CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, first.Status);
        Assert.Equal(UpdateStatus.Skipped, second.Status);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Network_failures_get_a_friendly_message()
    {
        var source = new FakeSource { Failure = new UpdateException(UpdateErrorKind.Network, "socket error 10065") };

        var result = await Service(source).CheckAsync(true, CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal("Couldn't check for updates. Check your connection and try again.", result.Message);
    }

    [Fact]
    public async Task Concurrent_checks_share_one_request()
    {
        var source = new FakeSource { Releases = [Release("0.2.0")], Gate = new TaskCompletionSource() };
        var service = Service(source);

        var first = service.CheckAsync(false, CancellationToken.None);
        var second = service.CheckAsync(true, CancellationToken.None);
        source.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task A_version_is_announced_once()
    {
        var service = Service(new FakeSource { Releases = [Release("0.2.0")] });
        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.True(service.ShouldNotify(result));
        service.MarkNotified(result.Release!.Version);
        Assert.False(service.ShouldNotify(result));
        Assert.False(Service(new FakeSource()).ShouldNotify(result));
    }

    [Fact]
    public async Task Remind_later_snoozes_that_version_but_not_newer_ones()
    {
        var service = Service(new FakeSource { Releases = [Release("0.2.0")] });
        var result = await service.CheckAsync(true, CancellationToken.None);

        service.RemindLater(result.Release!.Version);

        Assert.False(service.ShouldNotify(result));
        Assert.True(service.ShouldNotify(new UpdateCheckResult(UpdateStatus.Available, "", Release("0.3.0"))));
        _clock.Now += UpdateService.RemindDelay + TimeSpan.FromMinutes(1);
        Assert.True(service.ShouldNotify(result));
    }

    [Fact]
    public void Corrupt_state_files_are_ignored()
    {
        File.WriteAllText(Path.Combine(_directory, "updates.json"), "{ nope");
        Assert.Null(Store.Load().LastCheckedUtc);
    }

    [Fact]
    public void Update_settings_persist()
    {
        string path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path, Log.Null);
        store.Save(new KumoSettings { UpdateChannel = UpdateChannel.Beta, AutomaticUpdateChecks = false });

        var loaded = store.Load();

        Assert.Equal(UpdateChannel.Beta, loaded.UpdateChannel);
        Assert.False(loaded.AutomaticUpdateChecks);
        Assert.True(new KumoSettings().AutomaticUpdateChecks);
        Assert.Equal(UpdateChannel.Release, new KumoSettings().UpdateChannel);
    }
}
