using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using TechHub.Core.Services;
using TechHub.Infrastructure.Updates;

namespace TechHub.Tests;

public sealed class UpdateDownloadTests
{
    private static readonly byte[] Package = [1, 2, 3, 4, 5];
    private const string Releases = "https://github.com/marcoszhp/TechHub/releases";

    [Theory]
    [InlineData(UpdateInstallMode.User, "TechHub-User-Setup-1.3.0.exe")]
    [InlineData(UpdateInstallMode.Machine, "TechHub-Setup-1.3.0.exe")]
    public async Task DownloadsExactAssetToUniqueVerifiedFile(UpdateInstallMode mode, string name)
    {
        using var fixture = new Fixture(mode);
        var service = fixture.Service();
        var info = await service.CheckAsync();
        Assert.Equal(UpdateCheckStatus.Available, info.Status);
        Assert.EndsWith(name, info.DownloadUrl);
        var reports = new List<UpdateProgress>();
        var first = await service.DownloadAsync(info, new InlineProgress(p => reports.Add(p)));
        var second = await service.DownloadAsync(info);
        Assert.Equal(Package, await File.ReadAllBytesAsync(first.InstallerPath));
        Assert.NotEqual(first.InstallerPath, second.InstallerPath);
        Assert.Equal(32, Path.GetFileName(Path.GetDirectoryName(first.InstallerPath)!).Length);
        Assert.Equal(100d, reports[^1].Percent);
        Assert.Empty(Directory.GetFiles(fixture.Staging, "*.partial", SearchOption.AllDirectories));
        Assert.Equal(2, fixture.DownloadRequests);
    }

    [Theory]
    [InlineData("1.2.1")]
    [InlineData("1.2.0")]
    public async Task EqualOrOlderStableReleaseIsUpToDate(string version)
    {
        using var f = new Fixture { Version = version };
        var info = await f.Service().CheckAsync();
        Assert.Equal(UpdateCheckStatus.UpToDate, info.Status);
        Assert.False(info.IsAvailable);
    }

    [Theory]
    [InlineData(UpdateInstallMode.Portable)]
    [InlineData(UpdateInstallMode.Unknown)]
    public async Task UnsupportedModeDoesNotQuery(UpdateInstallMode mode)
    {
        using var f = new Fixture(mode);
        Assert.Equal(UpdateCheckStatus.Unsupported, (await f.Service().CheckAsync()).Status);
        Assert.Equal(0, f.Requests);
    }

    [Fact]
    public async Task NonX64IsManual()
    {
        using var f = new Fixture();
        Assert.Equal(UpdateCheckStatus.Unsupported, (await f.Service(Architecture.Arm64).CheckAsync()).Status);
        Assert.Equal(0, f.Requests);
    }

    [Theory]
    [InlineData("invalid-version")]
    [InlineData("zero-size")]
    [InlineData("oversize")]
    [InlineData("missing-digest")]
    [InlineData("wrong-host")]
    [InlineData("wrong-repository")]
    [InlineData("wrong-asset")]
    [InlineData("duplicate")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("query-error")]
    [InlineData("malformed-json")]
    [InlineData("missing-fields")]
    [InlineData("bare-tag")]
    public async Task BadQueryNeverClaimsUpToDate(string defect)
    {
        using var f = new Fixture { Defect = defect };
        var info = await f.Service().CheckAsync();
        Assert.Equal(UpdateCheckStatus.Failed, info.Status);
        Assert.False(info.IsAvailable);
        Assert.False(Directory.Exists(f.Staging));
    }

    [Theory]
    [InlineData("url")]
    [InlineData("version")]
    [InlineData("mode")]
    [InlineData("current")]
    [InlineData("status")]
    [InlineData("available")]
    [InlineData("release")]
    [InlineData("oversize")]
    [InlineData("digest")]
    [InlineData("size")]
    public async Task TamperedMetadataIsRejectedBeforeDiskIO(string field)
    {
        using var f = new Fixture();
        var service = f.Service();
        var info = await service.CheckAsync();
        info = field switch
        {
            "url" => info with { DownloadUrl = "https://evil.example/TechHub.exe" },
            "version" => info with { LatestVersion = "../../bad" },
            "mode" => info with { InstallMode = UpdateInstallMode.Machine },
            "current" => info with { CurrentVersion = "0.0.0" },
            "status" => info with { Status = UpdateCheckStatus.UpToDate },
            "available" => info with { IsAvailable = false },
            "release" => info with { ReleaseUrl = "https://evil.example/tag/v1.3.0" },
            "oversize" => info with { SizeBytes = GitHubUpdateService.MaximumSizeBytes + 1 },
            "digest" => info with { Sha256 = new string('a', 64) },
            _ => info with { SizeBytes = 6 }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        Assert.False(Directory.Exists(f.Staging));
        Assert.Equal(0, f.DownloadRequests);
    }

    [Fact]
    public async Task ChangedTrustedReleaseAndChangedModeAreRejected()
    {
        using var f = new Fixture();
        var service = f.Service();
        var info = await service.CheckAsync();
        f.Version = "1.4.0";
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        f.Mode = UpdateInstallMode.Machine;
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
        Assert.False(Directory.Exists(f.Staging));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("oversized-stream")]
    [InlineData("length")]
    public async Task InvalidPackageLeavesNoFinalOrPartialAndPreservesOtherFiles(string defect)
    {
        using var f = new Fixture { DownloadDefect = defect };
        Directory.CreateDirectory(f.Staging);
        var other = Path.Combine(f.Staging, "other.partial");
        File.WriteAllText(other, "preservar");
        var service = f.Service();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.DownloadAsync(await service.CheckAsync()));
        Assert.Equal("preservar", File.ReadAllText(other));
        Assert.Single(Directory.GetFiles(f.Staging, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancellationDuringStreamingRemovesOnlyOwnedPartial()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var service = f.Service();
        var info = await service.CheckAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(info,
            new InlineProgress(p => { if (p.BytesReceived > 0) cancellation.Cancel(); }), cancellation.Token));
        Assert.Empty(Directory.GetFiles(f.Staging, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CallerCancellationOfQueryPropagates()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service().CheckAsync(cancellation.Token));
        Assert.Equal(0, f.Requests);
    }

    [Fact]
    public async Task DownloadDeadlineCancelsStalledStreamAndCleansPartial()
    {
        using var f = new Fixture { DownloadDefect = "stall" };
        var service = f.Service(timeout: TimeSpan.FromMilliseconds(100));
        var info = await service.CheckAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(info));
        if (Directory.Exists(f.Staging)) Assert.Empty(Directory.GetFiles(f.Staging, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StagingAncestorsCannotBeReplacedDuringDownload()
    {
        using var f = new Fixture();
        var service = f.Service();
        var info = await service.CheckAsync();
        var attempted = false;
        var result = await service.DownloadAsync(info, new InlineProgress(p =>
        {
            if (p.BytesReceived == 0)
            {
                attempted = true;
                Assert.Throws<IOException>(() => Directory.Move(f.Staging, f.Staging + "-moved"));
            }
        }));
        Assert.True(attempted);
        Assert.True(File.Exists(result.InstallerPath));
    }

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/asset", true)]
    [InlineData("https://evil.example/asset", false)]
    [InlineData("http://release-assets.githubusercontent.com/asset", false)]
    [InlineData("https://release-assets.githubusercontent.com.evil.example/asset", false)]
    public async Task OnlyHttpsGitHubCdnResponseIsAccepted(string target, bool accepted)
    {
        using var f = new Fixture { RedirectTarget = target };
        var service = f.Service();
        var info = await service.CheckAsync();
        if (accepted) Assert.True(File.Exists((await service.DownloadAsync(info)).InstallerPath));
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(info));
            Assert.False(Directory.Exists(f.Staging));
        }
    }

    [Theory]
    [InlineData("1.3.0-beta")]
    [InlineData("1.3")]
    [InlineData("01.3.0")]
    [InlineData("1.3.0.0")]
    [InlineData("1.3.0/evil")]
    public async Task InvalidVersionCannotBecomePath(string version)
    {
        using var f = new Fixture { Version = version };
        Assert.Equal(UpdateCheckStatus.Failed, (await f.Service().CheckAsync()).Status);
        Assert.False(Directory.Exists(f.Staging));
    }

    [Fact]
    public void ModeDetectionRequiresStandardPathAndMatchingMarkers()
    {
        using var f = new Fixture();
        var local = Path.Combine(f.Root, "local");
        var programs = Path.Combine(f.Root, "programs");
        foreach (var (parent, mode, marker) in new[] { (local, UpdateInstallMode.User, "User"), (programs, UpdateInstallMode.Machine, "Machine") })
        {
            var dir = Path.Combine(parent, "TechHub");
            Directory.CreateDirectory(dir);
            Assert.Equal(UpdateInstallMode.Unknown, GitHubUpdateService.DetectInstallMode(dir, local, programs));
            File.WriteAllText(Path.Combine(dir, "TechHub.App.exe"), "fake");
            File.WriteAllText(Path.Combine(dir, "unins000.exe"), "fake");
            Assert.Equal(mode, GitHubUpdateService.DetectInstallMode(dir, local, programs));
            File.WriteAllText(Path.Combine(dir, "install-mode.txt"), marker.ToLowerInvariant());
            Assert.Equal(mode, GitHubUpdateService.DetectInstallMode(dir, local, programs));
            File.WriteAllText(Path.Combine(dir, "install-mode.txt"), marker);
            Assert.Equal(mode, GitHubUpdateService.DetectInstallMode(dir, local, programs));
            File.WriteAllText(Path.Combine(dir, "install-mode.txt"), "wrong");
            Assert.Equal(UpdateInstallMode.Unknown, GitHubUpdateService.DetectInstallMode(dir, local, programs));
            File.WriteAllText(Path.Combine(dir, "portable.flag"), "");
            Assert.Equal(UpdateInstallMode.Portable, GitHubUpdateService.DetectInstallMode(dir, local, programs));
        }
        var custom = Path.Combine(f.Root, "custom");
        Directory.CreateDirectory(custom);
        File.WriteAllText(Path.Combine(custom, "TechHub.App.exe"), "fake");
        File.WriteAllText(Path.Combine(custom, "unins000.exe"), "fake");
        File.WriteAllText(Path.Combine(custom, "install-mode.txt"), "user");
        Assert.Equal(UpdateInstallMode.Unknown, GitHubUpdateService.DetectInstallMode(custom, local, programs));
    }

    private sealed class InlineProgress(Action<UpdateProgress> report) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => report(value); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "TechHub-UpdateTests-" + Guid.NewGuid().ToString("N"));
        public string Staging => Path.Combine(Root, "Updates");
        public UpdateInstallMode Mode { get; set; }
        public string Version { get; set; } = "1.3.0";
        public string? Defect { get; set; }
        public string? DownloadDefect { get; set; }
        public string? RedirectTarget { get; set; }
        public int Requests { get; private set; }
        public int DownloadRequests { get; private set; }
        private readonly HttpClient _client;
        public Fixture(UpdateInstallMode mode = UpdateInstallMode.User)
        {
            Mode = mode;
            _client = new HttpClient(new Handler(Respond)) { Timeout = Timeout.InfiniteTimeSpan };
        }
        public GitHubUpdateService Service(Architecture architecture = Architecture.X64, TimeSpan? timeout = null) =>
            new(_client, "1.2.1", () => Mode, Staging, architecture, timeout);
        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Requests++;
            if (request.RequestUri!.Host == "api.github.com")
            {
                Assert.Equal("https://api.github.com/repos/marcoszhp/TechHub/releases/latest", request.RequestUri.AbsoluteUri);
                return new HttpResponseMessage(Defect == "query-error" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                { Content = new StringContent(Metadata()), RequestMessage = request };
            }
            DownloadRequests++;
            var bytes = DownloadDefect switch { "hash" => new byte[] { 5, 4, 3, 2, 1 }, "truncated" => Package[..3],
                "oversized-stream" => new byte[6], _ => Package };
            HttpContent content = DownloadDefect == "stall" ? new StreamContent(new StalledStream())
                : new StreamContent(new UnknownLengthStream(bytes));
            // Absent Content-Length exercises the actual streaming bound.
            if (DownloadDefect == "length") content.Headers.ContentLength = 6;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content,
                RequestMessage = RedirectTarget is null ? request : new HttpRequestMessage(HttpMethod.Get, RedirectTarget) };
        }
        private string Metadata()
        {
            if (Defect == "malformed-json") return "{";
            if (Defect == "missing-fields") return "{}";
            var version = Defect == "invalid-version" ? "../evil" : Version;
            var name = Mode == UpdateInstallMode.User ? $"TechHub-User-Setup-{version}.exe" : $"TechHub-Setup-{version}.exe";
            var url = $"{Releases}/download/v{version}/{name}";
            if (Defect == "wrong-host") url = url.Replace("github.com", "evil.example");
            if (Defect == "wrong-repository") url = url.Replace("marcoszhp/TechHub", "other/TechHub");
            var asset = new { name = Defect == "wrong-asset" ? "other.exe" : name, browser_download_url = url,
                size = Defect == "zero-size" ? 0L : Defect == "oversize" ? GitHubUpdateService.MaximumSizeBytes + 1 : Package.Length,
                digest = Defect == "missing-digest" ? null : "sha256:" + Convert.ToHexString(SHA256.HashData(Package)) };
            return JsonSerializer.Serialize(new { tag_name = Defect == "bare-tag" ? version : "v" + version, html_url = $"{Releases}/tag/v{version}",
                draft = Defect == "draft", prerelease = Defect == "prerelease", body = "Notas de versão",
                assets = Defect == "duplicate" ? new[] { asset, asset } : new[] { asset } });
        }
        public void Dispose()
        {
            _client.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UnknownLengthStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
