using System.Net;
using System.Security.Cryptography;
using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

public sealed class UpdateDownloaderTests : IDisposable
{
    private static readonly byte[] Payload = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
    private static readonly string PayloadHash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-download-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ReleaseAsset Asset(string name = "Kumo-0.2.0-win-x64-setup.exe", string url = "https://github.com/o/r/releases/download/v0.2.0/Kumo-0.2.0-win-x64-setup.exe", long size = 5000) =>
        new(name, new Uri(url), size);

    private static UpdateDownloader Downloader(FakeHandler handler) => new(new HttpClient(handler), "Kumo/test");

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    [Fact]
    public async Task Downloads_through_github_redirects_and_verifies_the_checksum()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host == "github.com"
            ? Redirect("https://release-assets.githubusercontent.com/blob/abc")
            : Bytes(Payload));

        string path = await Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_directory, "Kumo-0.2.0-win-x64-setup.exe"), path);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("https://evil.example.com/kumo.exe")]
    [InlineData("http://objects.githubusercontent.com/kumo.exe")]
    [InlineData("https://github.com.evil.example/kumo.exe")]
    [InlineData("https://objects.githubusercontent.com:8443/kumo.exe")]
    public async Task Redirects_to_untrusted_hosts_are_refused(string target)
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host == "github.com" ? Redirect(target) : Bytes(Payload));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(UpdateErrorKind.Integrity, error.Kind);
        Assert.Equal(1, handler.Calls);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Asset_urls_outside_github_are_refused_before_any_request()
    {
        var handler = new FakeHandler(_ => Bytes(Payload));

        await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(url: "https://example.com/x.exe"), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Redirect_loops_are_bounded()
    {
        var handler = new FakeHandler(_ => Redirect("https://github.com/o/r/again"));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(UpdateErrorKind.Malformed, error.Kind);
    }

    [Fact]
    public async Task Checksum_mismatch_leaves_nothing_behind()
    {
        var tampered = Payload.ToArray();
        tampered[10] ^= 0xFF;
        var handler = new FakeHandler(_ => Bytes(tampered));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(UpdateErrorKind.Integrity, error.Kind);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Truncated_downloads_are_rejected()
    {
        var handler = new FakeHandler(_ => Bytes(Payload[..1000]));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(UpdateErrorKind.Integrity, error.Kind);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Oversized_downloads_are_cut_off()
    {
        var handler = new FakeHandler(_ => Bytes(new byte[9000]));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(UpdateErrorKind.Integrity, error.Kind);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("..\\Kumo.exe")]
    [InlineData("sub/dir.exe")]
    public async Task Unsafe_asset_names_are_refused(string name)
    {
        var handler = new FakeHandler(_ => Bytes(Payload));

        await Assert.ThrowsAsync<UpdateException>(() => Downloader(handler).DownloadAsync(Asset(name: name), PayloadHash, _directory, null, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Cancellation_removes_the_partial_file()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(cancel)) });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, null, cancel.Token));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Progress_is_reported()
    {
        var handler = new FakeHandler(_ => Bytes(Payload));
        var reports = new List<DownloadProgress>();

        await Downloader(handler).DownloadAsync(Asset(), PayloadHash, _directory, new InlineProgress(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public void Checksum_files_are_parsed_strictly()
    {
        string hash = new('a', 64);
        var sums = UpdateDownloader.ParseChecksums($"{hash}  Kumo-0.2.0-win-x64.zip\n{hash} *SHA-file.exe\n\n");

        Assert.Equal(hash, sums["Kumo-0.2.0-win-x64.zip"]);
        Assert.Equal(hash, sums["SHA-file.exe"]);
        Assert.Throws<UpdateException>(() => UpdateDownloader.ParseChecksums("not a checksum line"));
        Assert.Throws<UpdateException>(() => UpdateDownloader.ParseChecksums($"{hash}  ../evil.exe"));
        Assert.Throws<UpdateException>(() => UpdateDownloader.ParseChecksums($"{hash}  a.zip\n{hash}  a.zip"));
    }

    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    private sealed class SlowStream(CancellationTokenSource cancel) : Stream
    {
        private int _reads;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (++_reads == 2)
                cancel.Cancel();
            return Math.Min(count, 100);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (++_reads == 2)
                cancel.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Math.Min(buffer.Length, 100);
        }
    }
}
