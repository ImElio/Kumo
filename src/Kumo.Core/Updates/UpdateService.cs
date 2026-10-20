using System.Text.Json;
using Kumo.Core.Diagnostics;

namespace Kumo.Core.Updates;

/// <summary>What this build and installation may do about updates.</summary>
public enum UpdateCapability
{
    /// <summary>No upstream repository was configured at build time; nothing is checked.</summary>
    Unconfigured,

    /// <summary>Installed from the Microsoft Store, which owns updates.</summary>
    StoreManaged,

    /// <summary>Kumo reports new versions and opens the release page; the user installs them.</summary>
    NotifyOnly,

    /// <summary>Kumo downloads, verifies and launches the signed installer.</summary>
    InstallVerified,
}

/// <param name="ExpectedPublisher">Certificate subject the installer must be signed by; empty disables installation.</param>
public sealed record UpdateEnvironment(
    SemVersion CurrentVersion,
    InstallationKind Installation,
    Architecture Architecture,
    RepositoryIdentity? Repository,
    string ExpectedPublisher)
{
    public UpdateCapability Capability => Installation switch
    {
        InstallationKind.Store => UpdateCapability.StoreManaged,
        _ when Repository is null => UpdateCapability.Unconfigured,
        InstallationKind.Installer when ExpectedPublisher.Length > 0 => UpdateCapability.InstallVerified,
        _ => UpdateCapability.NotifyOnly,
    };
}

public enum UpdateStatus
{
    Idle,
    Checking,
    Disabled,
    Skipped,
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(UpdateStatus Status, string Message, UpdateRelease? Release = null, UpdateErrorKind? Error = null);

/// <summary>The little that is remembered between checks. No identifiers, no history.</summary>
public sealed class UpdateState
{
    public DateTimeOffset? LastCheckedUtc { get; set; }

    public DateTimeOffset? RetryAfterUtc { get; set; }

    public string? NotifiedVersion { get; set; }

    public string? RemindAfterVersion { get; set; }

    public DateTimeOffset? RemindAfterUtc { get; set; }
}

public sealed class UpdateStateStore
{
    private readonly string _path;
    private readonly Log _log;

    public UpdateStateStore(string path, Log log)
    {
        _path = path;
        _log = log;
    }

    public UpdateState Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new UpdateState();
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, Settings.SettingsJsonContext.Default.UpdateState) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warning("Update state could not be read; starting fresh", ("detail", ex.Message));
            return new UpdateState();
        }
    }

    public void Save(UpdateState state)
    {
        try
        {
            string directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, $".updates.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(state, Settings.SettingsJsonContext.Default.UpdateState));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Update state could not be saved", ("detail", ex.Message));
        }
    }
}

/// <summary>
/// Decides when to look for updates and what to tell the user. Checks are one-off asynchronous requests: there is no
/// timer, no background thread and no connection kept open between them.
/// </summary>
public sealed class UpdateService
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RemindDelay = TimeSpan.FromDays(3);

    private readonly IUpdateSource? _source;
    private readonly UpdateStateStore _store;
    private readonly Func<UpdateChannel> _channel;
    private readonly Func<bool> _automaticChecks;
    private readonly TimeProvider _clock;
    private readonly Log _log;
    private readonly object _gate = new();
    private Task<UpdateCheckResult>? _inFlight;
    private UpdateChannel _inFlightChannel;
    private UpdateState _state;

    public UpdateService(UpdateEnvironment environment, IUpdateSource? source, UpdateStateStore store, Func<UpdateChannel> channel, Log log,
        TimeProvider? clock = null, Func<bool>? automaticChecks = null)
    {
        Environment = environment;
        _source = source;
        _store = store;
        _channel = channel;
        _automaticChecks = automaticChecks ?? (() => true);
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _state = store.Load();
    }

    /// <summary>Raised whenever <see cref="Status"/> changes, on whichever thread finished the work.</summary>
    public event Action<UpdateCheckResult>? StatusChanged;

    /// <summary>The latest state for display: Idle before the first check, Checking while one runs, then its outcome.</summary>
    public UpdateCheckResult Status { get; private set; } = new(UpdateStatus.Idle, string.Empty);

    public UpdateEnvironment Environment { get; }

    public UpdateCapability Capability => _source is null ? UpdateCapability.Unconfigured : Environment.Capability;

    public UpdateCheckResult? LastResult { get; private set; }

    /// <summary>
    /// Concurrent callers for the same channel share the check already in progress. A check for a different channel
    /// waits for the running one and then asks again, so its answer always reflects the channel that was selected.
    /// </summary>
    public Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var channel = _channel();
            if (_inFlight is { IsCompleted: false } running)
            {
                if (channel == _inFlightChannel)
                    return running;
                _inFlight = AfterAsync(running, channel, manual, cancellationToken);
            }
            else
            {
                _inFlight = RunCheckAsync(channel, manual, cancellationToken);
            }
            _inFlightChannel = channel;
            return _inFlight;
        }
    }

    /// <summary>
    /// The cached answer belongs to the previous channel, so it is dropped and a fresh check runs immediately. Nothing is
    /// downloaded or installed; an available update is only reported.
    /// </summary>
    public Task<UpdateCheckResult> ChannelChangedAsync(CancellationToken cancellationToken)
    {
        LastResult = null;
        _log.Info("Update channel changed", ("channel", _channel().ToString()));
        return CheckAsync(manual: true, cancellationToken);
    }

    public bool ShouldNotify(UpdateCheckResult result)
    {
        if (result.Status != UpdateStatus.Available || result.Release is null)
            return false;
        string version = result.Release.Version.ToShortString();
        if (version == _state.NotifiedVersion)
            return false;
        return !(version == _state.RemindAfterVersion && _state.RemindAfterUtc is { } until && _clock.GetUtcNow() < until);
    }

    public void MarkNotified(SemVersion version)
    {
        _state.NotifiedVersion = version.ToShortString();
        _store.Save(_state);
    }

    /// <summary>Snoozes reminders for this version; a newer version is still announced immediately.</summary>
    public void RemindLater(SemVersion version)
    {
        _state.RemindAfterVersion = version.ToShortString();
        _state.RemindAfterUtc = _clock.GetUtcNow() + RemindDelay;
        _state.NotifiedVersion = null;
        _store.Save(_state);
    }

    private async Task<UpdateCheckResult> AfterAsync(Task<UpdateCheckResult> previous, UpdateChannel channel, bool manual, CancellationToken cancellationToken)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The earlier check was for another channel; its outcome no longer matters.
        }
        return await RunCheckAsync(channel, manual, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateCheckResult> RunCheckAsync(UpdateChannel channel, bool manual, CancellationToken cancellationToken)
    {
        var before = Status;
        bool goesOnline = manual || IsAutomaticCheckDue();
        if (goesOnline && Capability is UpdateCapability.NotifyOnly or UpdateCapability.InstallVerified)
            Publish(new UpdateCheckResult(UpdateStatus.Checking, "Checking for updates…"));

        UpdateCheckResult result;
        try
        {
            result = await CheckCoreAsync(channel, manual, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Publish(before);
            throw;
        }

        Publish(result.Status == UpdateStatus.Skipped ? before : result);
        LastResult = result.Status == UpdateStatus.Skipped ? LastResult : result;
        _log.Info("Update check", ("status", result.Status.ToString()), ("manual", manual),
            ("version", result.Release?.Version.ToString()), ("error", result.Error?.ToString()));
        return result;
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(UpdateChannel channel, bool manual, CancellationToken cancellationToken)
    {
        switch (Capability)
        {
            case UpdateCapability.StoreManaged:
                return new UpdateCheckResult(UpdateStatus.Disabled, "Updates managed by Microsoft Store.");
            case UpdateCapability.Unconfigured:
                return new UpdateCheckResult(UpdateStatus.Disabled, "Update checks aren't available in this build.");
        }

        var now = _clock.GetUtcNow();
        if (!manual)
        {
            if (!_automaticChecks())
                return new UpdateCheckResult(UpdateStatus.Skipped, "Automatic checks are off.");
            if (_state.LastCheckedUtc is { } last && now - last < AutomaticInterval)
                return new UpdateCheckResult(UpdateStatus.Skipped, "Checked recently.");
            if (_state.RetryAfterUtc is { } retry && now < retry)
                return new UpdateCheckResult(UpdateStatus.Skipped, "Waiting for GitHub's rate limit to reset.");
        }

        try
        {
            var releases = await _source!.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
            _state.LastCheckedUtc = now;
            _state.RetryAfterUtc = null;
            _store.Save(_state);

            var update = ReleaseSelector.SelectUpdate(releases, Environment.CurrentVersion, channel, Environment.Installation, Environment.Architecture);
            return update is null
                ? new UpdateCheckResult(UpdateStatus.UpToDate, UpToDateMessage(channel))
                : new UpdateCheckResult(UpdateStatus.Available, $"Kumo {update.Version.ToShortString()} is available.", update);
        }
        catch (UpdateException ex)
        {
            if (ex.RetryAfter is { } retryAfter)
            {
                _state.RetryAfterUtc = retryAfter;
                _store.Save(_state);
            }
            _log.Warning("Update check failed", ("kind", ex.Kind.ToString()), ("detail", ex.InnerException?.Message ?? ex.Message));
            string message = ex.Kind switch
            {
                UpdateErrorKind.Network or UpdateErrorKind.Timeout => "Couldn't check for updates. Check your connection and try again.",
                UpdateErrorKind.RateLimited => ex.Message,
                _ => "Couldn't check for updates. Try again later.",
            };
            return new UpdateCheckResult(UpdateStatus.Failed, message, Error: ex.Kind);
        }
    }

    /// <summary>
    /// After returning to a channel that does not accept the installed pre-release, nothing is offered until a higher
    /// version on that channel exists. Kumo never moves to a lower version, so this is said plainly.
    /// </summary>
    private string UpToDateMessage(UpdateChannel channel)
    {
        var current = Environment.CurrentVersion;
        if (!current.IsPrerelease || ReleaseSelector.StagesFor(channel).Contains(current.Stage))
            return "Kumo is up to date.";
        string kind = channel == UpdateChannel.Release ? "stable release" : "version on this channel";
        return $"You're using the pre-release {current.ToShortString()}. Kumo will update when a newer {kind} is available.";
    }

    private bool IsAutomaticCheckDue()
    {
        var now = _clock.GetUtcNow();
        return _automaticChecks()
            && !(_state.LastCheckedUtc is { } last && now - last < AutomaticInterval)
            && !(_state.RetryAfterUtc is { } retry && now < retry);
    }

    private void Publish(UpdateCheckResult status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}
