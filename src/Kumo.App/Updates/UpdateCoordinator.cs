using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using Kumo.Core.Diagnostics;
using Kumo.Core.Updates;
using Kumo.Windows.Updates;
using Architecture = Kumo.Core.Updates.Architecture;

namespace Kumo.App.Updates;

/// <summary>
/// Wires the update pipeline for this process: build-time configuration, installation type, the GitHub source and
/// the verified download and handoff of the installer. Network objects are created only when a check runs.
/// </summary>
internal sealed class UpdateCoordinator : IDisposable
{
    private readonly Log _log;
    private readonly Lazy<HttpClient> _http;
    private readonly string _downloads;

    public UpdateCoordinator(Log log, string appDataRoot, Func<UpdateChannel> channel)
    {
        _log = log;
        _downloads = Path.Combine(appDataRoot, "updates");
        Environment = DetectEnvironment();
        _http = new Lazy<HttpClient>(CreateClient);
        IUpdateSource? source = Environment.Repository is { } repository && Environment.Installation != InstallationKind.Store
            ? new LazySource(() => new GitHubReleaseSource(_http.Value, repository, Environment.CurrentVersion.ToString()))
            : null;
        Service = new UpdateService(Environment, source, new UpdateStateStore(Path.Combine(appDataRoot, "updates.json"), log), channel, log);
    }

    public UpdateEnvironment Environment { get; }

    public UpdateService Service { get; }

    public string UserAgent => $"Kumo/{Environment.CurrentVersion}";

    public static SemVersion ProductVersion()
    {
        string? informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemVersion.TryParse(informational, out var version) ? version : SemVersion.Parse("0.0.0");
    }

    /// <summary>"0.2.0-beta.1 (9cb6b1f)": the version people see, with the short commit when the build records one.</summary>
    public static string DisplayVersion()
    {
        var version = ProductVersion();
        string commit = version.Build.Length >= 7 ? version.Build[..7] : version.Build;
        return commit.Length > 0 ? $"{version.ToShortString()} ({commit})" : version.ToShortString();
    }

    /// <summary>
    /// Downloads the installer for <paramref name="release"/>, checks it against the published SHA-256 list, then
    /// requires a valid Authenticode signature from the configured publisher and a matching product version.
    /// The checksum only detects corruption; the signature is what authenticates the file.
    /// </summary>
    public async Task<string> DownloadVerifiedInstallerAsync(UpdateRelease release, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (Service.Capability != UpdateCapability.InstallVerified)
            throw new UpdateException(UpdateErrorKind.Unsupported, "This copy of Kumo can't install updates by itself.");

        var asset = ReleaseAssets.For(release, InstallationKind.Installer, Environment.Architecture)
            ?? throw new UpdateException(UpdateErrorKind.Unsupported, "This release has no installer for your PC.");
        var checksumAsset = release.Assets.FirstOrDefault(a => a.Name == ReleaseAssets.Checksums)
            ?? throw new UpdateException(UpdateErrorKind.Integrity, "This release has no checksum list, so it can't be verified.");

        var downloader = new UpdateDownloader(_http.Value, UserAgent);
        var checksums = await downloader.GetChecksumsAsync(checksumAsset, cancellationToken).ConfigureAwait(false);
        if (!checksums.TryGetValue(asset.Name, out string? expected))
            throw new UpdateException(UpdateErrorKind.Integrity, "The installer isn't listed in the release checksums.");

        string directory = Path.Combine(_downloads, release.Version.ToShortString());
        string path = await downloader.DownloadAsync(asset, expected, directory, progress, cancellationToken).ConfigureAwait(false);

        var signature = Authenticode.Verify(path);
        string? productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        bool versionMatches = productVersion != null && SemVersion.TryParse(productVersion, out var fileVersion) && fileVersion.Equals(release.Version);
        if (!signature.IsSignedBy(Environment.ExpectedPublisher) || !versionMatches)
        {
            _log.Warning("Downloaded installer rejected", ("trusted", signature.IsTrusted), ("status", signature.Status),
                ("subject", signature.Subject), ("productVersion", productVersion));
            TryDelete(path);
            throw new UpdateException(UpdateErrorKind.Signature, "The downloaded installer couldn't be verified, so it wasn't run.");
        }
        _log.Info("Installer verified", ("version", release.Version.ToString()));
        return path;
    }

    /// <summary>Starts the verified installer as a separate, non-elevated process; the caller then exits Kumo.</summary>
    public void LaunchInstaller(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
        _log.Info("Installer launched");
    }

    /// <summary>Removes downloads left by an earlier session, including partial or interrupted ones.</summary>
    public void CleanUpDownloads()
    {
        if (!Directory.Exists(_downloads))
            return;
        try
        {
            Directory.Delete(_downloads, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An installer that is still running keeps its own file open; the next start removes it.
            _log.Debug("Old update downloads not removed yet", ("detail", ex.Message));
        }
    }

    public void Dispose()
    {
        if (_http.IsValueCreated)
            _http.Value.Dispose();
    }

    private static UpdateEnvironment DetectEnvironment()
    {
        bool packaged = PackageIdentity.IsPackaged();
        string marker = Path.Combine(AppContext.BaseDirectory, InstallationSource.MarkerFileName);
        string? markerJson = !packaged && File.Exists(marker) ? File.ReadAllText(marker) : null;
        var kind = InstallationSource.Resolve(packaged, packaged && IsStoreSigned(), markerJson);

        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value ?? string.Empty);
        RepositoryIdentity? repository = RepositoryIdentity.TryParse(metadata.GetValueOrDefault("KumoUpdateRepository"), out var identity) ? identity : null;
        var architecture = RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? Architecture.Arm64 : Architecture.X64;
        return new UpdateEnvironment(ProductVersion(), kind, architecture, repository, metadata.GetValueOrDefault("KumoUpdatePublisher", string.Empty));
    }

    private static bool IsStoreSigned()
    {
        try
        {
            return global::Windows.ApplicationModel.Package.Current.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.Store;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Redirects are followed by UpdateDownloader so it can check every host; idle connections close after a minute so
    // nothing stays open between checks.
    private static HttpClient CreateClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The rejected file is removed with the rest of the download folder on the next start.
        }
    }

    private sealed class LazySource(Func<IUpdateSource> create) : IUpdateSource
    {
        private readonly Lazy<IUpdateSource> _source = new(create);

        public Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(CancellationToken cancellationToken) => _source.Value.GetReleasesAsync(cancellationToken);
    }
}
