using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using TechHub.Core.Services;
using TechHub.Core.Updates;
using TechHub.Updater;

namespace TechHub.Ui.Tests;

public sealed class UpdaterRuntimeTests
{
    private static readonly UpdateJob Job = new(1, 1, "C:\\isolated-fixture", UpdateInstallMode.User, "1.2.1", "1.3.0", new string('a', 64), 5);

    [Theory]
    [InlineData(UpdateInstallMode.User, "open")]
    [InlineData(UpdateInstallMode.Machine, "runas")]
    public void InstallerArgumentsAreFixedAndNeverForceCloseOrReboot(UpdateInstallMode mode, string verb)
    {
        var start = WindowsUpdateRuntime.CreateInstallerStartInfo(Job with { Mode = mode }, "C:\\staged fixture");
        Assert.Equal(verb, start.Verb);
        Assert.Contains("/NORESTART", start.ArgumentList);
        Assert.Contains("/NOCLOSEAPPLICATIONS", start.ArgumentList);
        Assert.Contains("/NOFORCECLOSEAPPLICATIONS", start.ArgumentList);
        Assert.Contains("/NORESTARTAPPLICATIONS", start.ArgumentList);
        Assert.DoesNotContain("/FORCECLOSEAPPLICATIONS", start.ArgumentList);
        Assert.Equal("/DIR=" + Job.InstallDirectory, start.ArgumentList.Single(a => a.StartsWith("/DIR=")));
    }

    [Theory]
    [InlineData(false, false, "1.3.0", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 5, true)]
    [InlineData(true, false, "1.3.0", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 5, false)]
    [InlineData(false, true, "1.3.0", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 5, false)]
    [InlineData(false, false, "1.3.1", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 5, false)]
    [InlineData(false, false, "1.3.0", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 5, false)]
    [InlineData(false, false, "1.3.0", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 6, false)]
    public async Task HelperRevalidatesStableVersionSizeAndDigest(bool draft, bool prerelease, string version, string digest, long size, bool accepted)
    {
        var body = JsonSerializer.Serialize(new { draft, prerelease, tag_name = "v" + version,
            assets = new[] { new { name = UpdatePolicy.PackageName(Job), size, digest } } });
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var runtime = new WindowsUpdateRuntime(releaseClient: client);
        if (accepted) await runtime.VerifyReleaseAsync(Job, CancellationToken.None);
        else await Assert.ThrowsAsync<InvalidDataException>(() => runtime.VerifyReleaseAsync(Job, CancellationToken.None));
    }

    [Fact]
    public async Task RedirectedMetadataIsRejected()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/evil"), Content = new StringContent("{}")
        }));
        using var runtime = new WindowsUpdateRuntime(releaseClient: client);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.VerifyReleaseAsync(Job, CancellationToken.None));
    }

    [Fact]
    public async Task OversizedMetadataIsRejected()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) }));
        using var runtime = new WindowsUpdateRuntime(releaseClient: client);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.VerifyReleaseAsync(Job, CancellationToken.None));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
