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
    private readonly TimeProvider _clock;
    private readonly Log _log;
    private readonly object _gate = new();
    private Task<UpdateCheckResult>? _inFlight;
    private UpdateState _state;

    public UpdateService(UpdateEnvironment environment, IUpdateSource? source, UpdateStateStore store, Func<UpdateChannel> channel, Log log, TimeProvider? clock = null)
    {
        Environment = environment;
        _source = source;
        _store = store;
        _channel = channel;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _state = store.Load();
    }

    public UpdateEnvironment Environment { get; }

    public UpdateCapability Capability => _source is null ? UpdateCapability.Unconfigured : Environment.Capability;

    public UpdateCheckResult? LastResult { get; private set; }

    /// <summary>Concurrent callers share the check already in progress instead of starting another request.</summary>
    public Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false })
                return _inFlight;
            _inFlight = RunCheckAsync(manual, cancellationToken);
            return _inFlight;
        }
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

    private async Task<UpdateCheckResult> RunCheckAsync(bool manual, CancellationToken cancellationToken)
    {
        var result = await CheckCoreAsync(manual, cancellationToken).ConfigureAwait(false);
        LastResult = result.Status == UpdateStatus.Skipped ? LastResult : result;
        _log.Info("Update check", ("status", result.Status.ToString()), ("manual", manual),
            ("version", result.Release?.Version.ToString()), ("error", result.Error?.ToString()));
        return result;
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        switch (Capability)
        {
            case UpdateCapability.StoreManaged:
                return new UpdateCheckResult(UpdateStatus.Disabled, "Updates are managed by Microsoft Store.");
            case UpdateCapability.Unconfigured:
                return new UpdateCheckResult(UpdateStatus.Disabled, "Update checks aren't available in this build.");
        }

        var now = _clock.GetUtcNow();
        if (!manual)
        {
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

            var update = ReleaseSelector.SelectUpdate(releases, Environment.CurrentVersion, _channel(), Environment.Installation, Environment.Architecture);
            return update is null
                ? new UpdateCheckResult(UpdateStatus.UpToDate, "Kumo is up to date.")
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
            string message = ex.Kind is UpdateErrorKind.Network or UpdateErrorKind.Timeout
                ? "Couldn't check for updates. Check your connection and try again."
                : ex.Message;
            return new UpdateCheckResult(UpdateStatus.Failed, message, Error: ex.Kind);
        }
    }
}
