using System.Net;
using Kumo.Core.Updates;
using static Kumo.Core.Tests.Updates.UpdateFixtures;

namespace Kumo.Core.Tests.Updates;

public class GitHubReleaseSourceTests
{
    private static GitHubReleaseSource Source(FakeHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), Repository, "0.1.0", timeout);

    [Fact]
    public async Task Reads_published_releases_from_the_configured_repository_only()
    {
        var handler = new FakeHandler(_ => JsonResponse(new[]
        {
            Json("v0.2.0", assets: ["Kumo-0.2.0-win-x64.zip", "SHA256SUMS.txt"]),
            Json("v0.2.0-beta.1", prerelease: true),
        }));

        var releases = await Source(handler).GetReleasesAsync(CancellationToken.None);

        Assert.Equal(["0.2.0", "0.2.0-beta.1"], releases.Select(r => r.Version.ToString()));
        Assert.Equal(2, releases[0].Assets.Count);
        Assert.True(releases[1].IsPrerelease);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/kumo-test/kumo/releases?per_page=100&page=1", request.ToString());
    }

    [Fact]
    public async Task Sends_a_descriptive_user_agent_and_no_credentials()
    {
        HttpRequestMessage? seen = null;
        var handler = new FakeHandler(request =>
        {
            seen = request;
            return JsonResponse(Array.Empty<object>());
        });

        await Source(handler).GetReleasesAsync(CancellationToken.None);

        Assert.Contains("Kumo/0.1.0", seen!.Headers.UserAgent.ToString());
        Assert.Null(seen.Headers.Authorization);
    }

    [Fact]
    public async Task Follows_pages_until_a_short_page()
    {
        var handler = new FakeHandler(request =>
        {
            bool first = request.RequestUri!.Query.EndsWith("&page=1", StringComparison.Ordinal);
            var page = Enumerable.Range(0, first ? GitHubReleaseSource.PageSize : 3)
                .Select(i => Json(first ? $"v0.0.{i}" : $"v1.0.{i}"))
                .ToArray();
            return JsonResponse(page);
        });

        var releases = await Source(handler).GetReleasesAsync(CancellationToken.None);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(GitHubReleaseSource.PageSize + 3, releases.Count);
        Assert.Contains(releases, r => r.Version.ToString() == "1.0.2");
    }

    [Fact]
    public async Task Pagination_is_bounded()
    {
        var handler = new FakeHandler(_ => JsonResponse(Enumerable.Range(0, GitHubReleaseSource.PageSize).Select(i => Json($"v0.0.{i}")).ToArray()));

        await Source(handler).GetReleasesAsync(CancellationToken.None);

        Assert.Equal(GitHubReleaseSource.MaxPages, handler.Calls);
    }

    [Fact]
    public async Task Malformed_entries_are_skipped_without_losing_valid_ones()
    {
        var handler = new FakeHandler(_ => JsonResponse(new object[]
        {
            Json("release-1"),
            Json("v1.2"),
            new { tag_name = 42 },
            new { tag_name = "v0.3.0", html_url = "http://insecure.example/x" },
            Json("v0.3.0"),
        }));

        var releases = await Source(handler).GetReleasesAsync(CancellationToken.None);

        Assert.Equal(["0.3.0"], releases.Select(r => r.Version.ToString()));
    }

    [Fact]
    public async Task Non_array_responses_are_malformed()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { message = "hello" }));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(UpdateErrorKind.Malformed, error.Kind);
    }

    [Fact]
    public async Task Invalid_json_is_malformed()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{") });

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(UpdateErrorKind.Malformed, error.Kind);
    }

    [Fact]
    public async Task Rate_limiting_is_recognised_with_its_reset_time()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", "1893456000");
            return response;
        });

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(UpdateErrorKind.RateLimited, error.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), error.RetryAfter);
    }

    [Fact]
    public async Task Status_429_is_rate_limiting()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(UpdateErrorKind.RateLimited, error.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, UpdateErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, UpdateErrorKind.Server)]
    [InlineData(HttpStatusCode.Forbidden, UpdateErrorKind.Server)]
    public async Task Http_errors_are_classified(HttpStatusCode status, UpdateErrorKind expected)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public async Task Network_failures_are_reported_as_network_errors()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("no route"));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(UpdateErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Slow_responses_time_out()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var error = await Assert.ThrowsAsync<UpdateException>(() => Source(handler, TimeSpan.FromMilliseconds(100)).GetReleasesAsync(CancellationToken.None));
        Assert.Equal(UpdateErrorKind.Timeout, error.Kind);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_mistaken_for_a_timeout()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(handler).GetReleasesAsync(cancel.Token));
    }

    [Theory]
    [InlineData("owner/repo", true)]
    [InlineData("Im-Elio/Kumo.app", true)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("-owner/repo", false)]
    [InlineData("https://github.com/owner/repo", false)]
    [InlineData("", false)]
    public void Repository_identity_is_validated(string text, bool expected)
    {
        Assert.Equal(expected, RepositoryIdentity.TryParse(text, out _));
    }
}
