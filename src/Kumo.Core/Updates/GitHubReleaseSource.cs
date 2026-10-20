using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kumo.Core.Updates;

public enum UpdateErrorKind
{
    Network,
    Timeout,
    RateLimited,
    NotFound,
    Server,
    Malformed,
    Integrity,
    Signature,
    Unsupported,
}

/// <summary>A failure with a short message meant for people; details go to the log.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(UpdateErrorKind kind, string message, Exception? inner = null, DateTimeOffset? retryAfter = null)
        : base(message, inner)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    public UpdateErrorKind Kind { get; }

    public DateTimeOffset? RetryAfter { get; }
}

/// <summary>The upstream repository, fixed at build time. Updates are only ever read from this one repository.</summary>
public sealed partial record RepositoryIdentity(string Owner, string Name)
{
    public static bool TryParse(string? text, out RepositoryIdentity identity)
    {
        identity = null!;
        var match = text == null ? Match.Empty : Pattern().Match(text);
        if (!match.Success)
            return false;
        identity = new RepositoryIdentity(match.Groups[1].Value, match.Groups[2].Value);
        return true;
    }

    public Uri ReleasesPage => new($"https://github.com/{Owner}/{Name}/releases");

    public override string ToString() => $"{Owner}/{Name}";

    [GeneratedRegex(@"^([A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)/([A-Za-z0-9._-]{1,100})$")]
    private static partial Regex Pattern();
}

public interface IUpdateSource
{
    Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads published releases from the GitHub REST API without authentication. <c>/releases/latest</c> only returns
/// the newest stable release, so the full list is read and filtered by channel and SemVer instead.
/// </summary>
public sealed class GitHubReleaseSource : IUpdateSource
{
    public const int PageSize = 100;
    public const int MaxPages = 3;

    private readonly HttpClient _http;
    private readonly RepositoryIdentity _repository;
    private readonly string _userAgent;
    private readonly TimeSpan _timeout;

    public GitHubReleaseSource(HttpClient http, RepositoryIdentity repository, string productVersion, TimeSpan? timeout = null)
    {
        _http = http;
        _repository = repository;
        _userAgent = $"Kumo/{productVersion} (+https://github.com/{repository})";
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        var releases = new List<UpdateRelease>();
        for (int page = 1; page <= MaxPages; page++)
        {
            var uri = new Uri(FormattableString.Invariant(
                $"https://api.github.com/repos/{_repository.Owner}/{_repository.Name}/releases?per_page={PageSize}&page={page}"));
            using var document = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new UpdateException(UpdateErrorKind.Malformed, "GitHub returned an unexpected response.");

            int count = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                count++;
                if (TryReadRelease(element, out var release))
                    releases.Add(release);
            }
            if (count < PageSize)
                break;
        }
        return releases;
    }

    internal static bool TryReadRelease(JsonElement element, out UpdateRelease release)
    {
        release = null!;
        try
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("tag_name", out var tag)
                || !ReleaseTag.TryParse(tag.GetString(), out var platform, out var version)
                || !element.TryGetProperty("html_url", out var page)
                || !Uri.TryCreate(page.GetString(), UriKind.Absolute, out var pageUri)
                || pageUri.Scheme != Uri.UriSchemeHttps)
                return false;

            var assets = new List<ReleaseAsset>();
            if (element.TryGetProperty("assets", out var assetList) && assetList.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetList.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) && name.GetString() is { } assetName
                        && asset.TryGetProperty("browser_download_url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var downloadUri)
                        && asset.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) && bytes >= 0)
                        assets.Add(new ReleaseAsset(assetName, downloadUri, bytes));
                }
            }

            release = new UpdateRelease(
                platform,
                version,
                tag.GetString()!,
                String(element, "name") ?? tag.GetString()!,
                String(element, "body") ?? string.Empty,
                pageUri,
                element.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(published.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null,
                element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
                element.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
                assets);
            return true;
        }
        catch (InvalidOperationException)
        {
            // A property of an unexpected JSON type: this entry is malformed, the others may still be usable.
            return false;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException(UpdateErrorKind.Timeout, "GitHub took too long to respond.");
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException(UpdateErrorKind.Network, "Couldn't reach GitHub.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw FromStatus(response);
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new UpdateException(UpdateErrorKind.Malformed, "GitHub returned an unexpected response.", ex);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateException(UpdateErrorKind.Timeout, "GitHub took too long to respond.");
            }
        }
    }

    private static UpdateException FromStatus(HttpResponseMessage response)
    {
        var status = response.StatusCode;
        bool exhausted = Header(response, "x-ratelimit-remaining") == "0";
        if (status == HttpStatusCode.TooManyRequests || (status == HttpStatusCode.Forbidden && exhausted))
        {
            DateTimeOffset? reset = long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : null;
            return new UpdateException(UpdateErrorKind.RateLimited, "GitHub is limiting requests right now. Try again later.", retryAfter: reset);
        }
        if (status == HttpStatusCode.NotFound)
            return new UpdateException(UpdateErrorKind.NotFound, "The update source could not be found.");
        return new UpdateException(UpdateErrorKind.Server, FormattableString.Invariant($"GitHub returned an error ({(int)status})."));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
