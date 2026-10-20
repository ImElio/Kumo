using System.Net;
using System.Text;
using System.Text.Json;
using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

internal static class UpdateFixtures
{
    public static readonly RepositoryIdentity Repository = new("kumo-test", "kumo");

    public static UpdateRelease Release(string version, bool? prerelease = null, bool draft = false, bool portable = true, bool installer = true, Architecture architecture = Architecture.X64,
        ReleasePlatform platform = ReleasePlatform.Windows)
    {
        var parsed = SemVersion.Parse(version);
        var assets = new List<ReleaseAsset>();
        if (portable)
            assets.Add(Asset(ReleaseAssets.PortableName(parsed, architecture)));
        if (installer)
            assets.Add(Asset(ReleaseAssets.InstallerName(parsed, architecture)));
        assets.Add(Asset(ReleaseAssets.Checksums));
        string tag = ReleaseTag.Format(platform, parsed);
        return new UpdateRelease(platform, parsed, tag, "Kumo " + version, "Notes for " + version,
            new Uri($"https://github.com/kumo-test/kumo/releases/tag/{tag}"), DateTimeOffset.UnixEpoch, draft, prerelease ?? parsed.IsPrerelease, assets);
    }

    public static ReleaseAsset Asset(string name, long size = 1000) =>
        new(name, new Uri($"https://github.com/kumo-test/kumo/releases/download/v/{name}"), size);

    /// <summary>GitHub's release JSON for a version, in the shape the REST API returns.</summary>
    public static object Json(string tag, bool prerelease = false, bool draft = false, params string[] assets) => new
    {
        tag_name = tag,
        name = "Kumo " + tag,
        body = "Changes in " + tag,
        html_url = $"https://github.com/kumo-test/kumo/releases/tag/{tag}",
        draft,
        prerelease,
        published_at = "2026-10-01T12:00:00Z",
        assets = assets.Select(a => new { name = a, size = 1234, browser_download_url = $"https://github.com/kumo-test/kumo/releases/download/{tag}/{a}" }).ToArray(),
    };

    public static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

/// <summary>Answers HTTP requests from a script, so no test ever reaches the network.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _calls;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public int Calls => _calls;

    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests)
            Requests.Add(request.RequestUri!);
        return respond(request, cancellationToken);
    }
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeSource : IUpdateSource
{
    private int _calls;

    public IReadOnlyList<UpdateRelease> Releases { get; set; } = [];

    public Exception? Failure { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public int Calls => _calls;

    public async Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Gate != null)
            await Gate.Task.WaitAsync(cancellationToken);
        if (Failure != null)
            throw Failure;
        return Releases;
    }
}
