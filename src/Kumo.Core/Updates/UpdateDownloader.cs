using System.Globalization;
using System.Net;
using System.Security.Cryptography;

namespace Kumo.Core.Updates;

public sealed record DownloadProgress(long Received, long Total)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Received / Total, 0, 1);
}

/// <summary>
/// Downloads release assets. Redirects are followed by hand so every hop can be checked against the GitHub hosts
/// that serve release assets; the HttpClient must be created with automatic redirects disabled.
/// </summary>
public sealed class UpdateDownloader
{
    public const long MaxAssetBytes = 300L * 1024 * 1024;
    public const long MaxChecksumBytes = 64 * 1024;
    private const int MaxRedirects = 5;

    // github.com issues the browser_download_url; the file itself is served from GitHub's asset CDN hosts.
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    private readonly HttpClient _http;
    private readonly string _userAgent;

    public UpdateDownloader(HttpClient http, string userAgent)
    {
        _http = http;
        _userAgent = userAgent;
    }

    public static bool IsAllowed(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && AllowedHosts.Contains(uri.Host);

    /// <summary>Reads <c>SHA256SUMS.txt</c> into a map of file name → lowercase hex digest.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetChecksumsAsync(ReleaseAsset checksums, CancellationToken cancellationToken)
    {
        if (checksums.Size > MaxChecksumBytes)
            throw new UpdateException(UpdateErrorKind.Malformed, "The release checksum file is too large.");
        using var buffer = new MemoryStream();
        await DownloadToAsync(checksums.DownloadUrl, buffer, MaxChecksumBytes, null, cancellationToken).ConfigureAwait(false);
        return ParseChecksums(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    public static IReadOnlyDictionary<string, string> ParseChecksums(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            int space = line.IndexOf(' ');
            if (space != 64 || !line[..64].All(Uri.IsHexDigit))
                throw new UpdateException(UpdateErrorKind.Malformed, "The release checksum file is malformed.");
            string name = line[(space + 1)..].TrimStart(' ', '*');
            if (!ReleaseAssets.IsSafeFileName(name) || !result.TryAdd(name, line[..64].ToLowerInvariant()))
                throw new UpdateException(UpdateErrorKind.Malformed, "The release checksum file is malformed.");
        }
        return result;
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="directory"/> and checks its size and SHA-256 digest.
    /// The file only appears under its final name once complete and verified; otherwise nothing is left behind.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string expectedSha256, string directory, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!ReleaseAssets.IsSafeFileName(asset.Name))
            throw new UpdateException(UpdateErrorKind.Malformed, "The update file has an unexpected name.");
        if (asset.Size <= 0 || asset.Size > MaxAssetBytes)
            throw new UpdateException(UpdateErrorKind.Malformed, "The update file has an unexpected size.");

        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, asset.Name);
        string partial = target + ".part";
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(target)), Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateErrorKind.Malformed, "The update file has an unexpected name.");

        try
        {
            string digest;
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                digest = await DownloadToAsync(asset.DownloadUrl, file, asset.Size, progress, cancellationToken).ConfigureAwait(false);
                if (file.Length != asset.Size)
                    throw new UpdateException(UpdateErrorKind.Integrity, "The update download is incomplete.");
            }
            if (!string.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException(UpdateErrorKind.Integrity, "The update download doesn't match its published checksum.");

            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    private async Task<string> DownloadToAsync(Uri uri, Stream destination, long limit, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowed(uri))
                throw new UpdateException(UpdateErrorKind.Integrity, "The update is hosted somewhere Kumo doesn't trust.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new UpdateException(UpdateErrorKind.Network, "The download failed. Check your connection and try again.", ex);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location
                        ?? throw new UpdateException(UpdateErrorKind.Malformed, "The download was redirected without a destination.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new UpdateException(UpdateErrorKind.Server, FormattableString.Invariant($"The download failed ({(int)response.StatusCode})."));
                if (response.Content.Headers.ContentLength is { } length && length > limit)
                    throw new UpdateException(UpdateErrorKind.Integrity, "The update download is larger than expected.");

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                byte[] buffer = new byte[81920];
                long received = 0;
                int read;
                try
                {
                    while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        received += read;
                        if (received > limit)
                            throw new UpdateException(UpdateErrorKind.Integrity, "The update download is larger than expected.");
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        progress?.Report(new DownloadProgress(received, limit));
                    }
                }
                catch (HttpIOException ex)
                {
                    throw new UpdateException(UpdateErrorKind.Network, "The download was interrupted. Try again.", ex);
                }
                return Convert.ToHexString(hash.GetHashAndReset()).ToLower(CultureInfo.InvariantCulture);
            }
        }
        throw new UpdateException(UpdateErrorKind.Malformed, "The download was redirected too many times.");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
