using System.Security.Cryptography;
using System.Text.Json;
using TechHub.Core.Services;
using TechHub.Core.Updates;

namespace TechHub.Tests;

public sealed class UpdateApplyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TechHub-apply-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = [1, 2, 3, 4, 5];
    private UpdateJob Job => new(123, 456, "C:\\unused", UpdateInstallMode.User, "1.2.1", "1.3.0", Convert.ToHexString(SHA256.HashData(Payload)), Payload.Length);
    public UpdateApplyTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "apply-authorized"), "fixture");
    }
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("1.3")]
    [InlineData("1.3.0.1")]
    [InlineData("../1.3.0")]
    [InlineData("1.3.0-beta")]
    [InlineData("01.3.0")]
    public void InvalidVersionsAreRejected(string value) => Assert.Throws<InvalidDataException>(() => UpdatePolicy.ParseVersion(value));

    [Theory]
    [InlineData(UpdateInstallMode.User, "TechHub-User-Setup-1.3.0.exe")]
    [InlineData(UpdateInstallMode.Machine, "TechHub-Setup-1.3.0.exe")]
    public void ModesSelectDistinctInstaller(UpdateInstallMode mode, string expected) => Assert.Equal(expected, UpdatePolicy.PackageName(Job with { Mode = mode }));

    [Fact]
    public void PortableCannotBecomeInstalled() => Assert.Throws<InvalidDataException>(() => UpdatePolicy.DefaultDirectory(UpdateInstallMode.Portable));

    [Fact]
    public void CustomInstallAndWrongStagingAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => UpdatePolicy.Validate(Job, directory));
        Assert.Throws<InvalidDataException>(() => UpdatePolicy.Validate(Job with { InstallDirectory = UpdatePolicy.DefaultDirectory(UpdateInstallMode.User) }, directory));
    }

    [Fact]
    public async Task AbandonedAttemptDoesNotAcquireUpdateMarkerOrInstall()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        File.WriteAllText(Path.Combine(directory, "aborted"), "handoff timed out");
        var runtime = new FakeRuntime();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory));
        Assert.Equal(new[] { "wait" }, runtime.Calls);
    }

    [Fact]
    public async Task VerifiedSequencePreservesDataAndRecordsSuccessBeforeRestart()
    {
        var data = Path.Combine(directory, "user-data-sentinel");
        await File.WriteAllTextAsync(data, "saved-work");
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        var runtime = new FakeRuntime { OnRestart = () => Assert.True(JsonSerializer.Deserialize<UpdateApplyResult>(File.ReadAllText(Path.Combine(directory, "result.json")))!.Success) };
        var result = await new UpdateApplyEngine(runtime).ApplyAsync(Job, directory);
        Assert.True(result.Success);
        Assert.Equal(new[] { "wait", "instances", "trust", "install", "version", "restart" }, runtime.Calls);
        Assert.Equal("saved-work", await File.ReadAllTextAsync(data));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TamperingOrTruncationNeverInvokesInstaller(bool truncate)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), truncate ? [1, 2] : [1, 2, 3, 4, 6]);
        var runtime = new FakeRuntime();
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory));
        Assert.Empty(runtime.Calls);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(3010)]
    public async Task InstallerFailureOrRebootDoesNotRestart(int code)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        var runtime = new FakeRuntime { ExitCode = code };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory));
        Assert.DoesNotContain("restart", runtime.Calls);
        Assert.False(File.Exists(Path.Combine(directory, "result.json")));
    }

    [Fact]
    public async Task IncorrectInstalledVersionDoesNotRestart()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        var runtime = new FakeRuntime { InstalledVersion = "1.2.1" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory));
        Assert.DoesNotContain("restart", runtime.Calls);
    }

    [Fact]
    public async Task ParentTimeoutDoesNotInstall()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        var runtime = new FakeRuntime { ParentFailure = new TimeoutException() };
        await Assert.ThrowsAsync<TimeoutException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory));
        Assert.DoesNotContain("install", runtime.Calls);
    }

    [Fact]
    public async Task OfflineTrustCheckDoesNotInstall()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        var runtime = new FakeRuntime { TrustFailure = new HttpRequestException("offline") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory));
        Assert.DoesNotContain("install", runtime.Calls);
    }

    [Fact]
    public async Task DeadlineDuringInstallationDoesNotPreventSuccessfulRestart()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        using var deadline = new CancellationTokenSource();
        var runtime = new FakeRuntime { OnInstall = deadline.Cancel };
        var result = await new UpdateApplyEngine(runtime).ApplyAsync(Job, directory, deadline.Token);
        Assert.True(result.Success);
        Assert.Contains("restart", runtime.Calls);
    }

    [Fact]
    public async Task CancellationBeforeVerificationDoesNotInstall()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, UpdatePolicy.PackageName(Job)), Payload);
        var runtime = new FakeRuntime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateApplyEngine(runtime).ApplyAsync(Job, directory, cancellation.Token));
        Assert.Empty(runtime.Calls);
    }

    private sealed class FakeRuntime : IUpdateApplyRuntime
    {
        public List<string> Calls { get; } = [];
        public int ExitCode { get; init; }
        public string InstalledVersion { get; init; } = "1.3.0";
        public Exception? ParentFailure { get; init; }
        public Exception? TrustFailure { get; init; }
        public Action? OnRestart { get; init; }
        public Action? OnInstall { get; init; }
        public Task WaitForParentAsync(UpdateJob job, CancellationToken token) { Calls.Add("wait"); return ParentFailure is null ? Task.CompletedTask : Task.FromException(ParentFailure); }
        public void AssertNoOtherInstances() => Calls.Add("instances");
        public Task VerifyReleaseAsync(UpdateJob job, CancellationToken token) { Calls.Add("trust"); return TrustFailure is null ? Task.CompletedTask : Task.FromException(TrustFailure); }
        public Task<int> InstallAsync(UpdateJob job, string path, CancellationToken token) { Calls.Add("install"); OnInstall?.Invoke(); return Task.FromResult(ExitCode); }
        public string ReadInstalledVersion(UpdateJob job) { Calls.Add("version"); return InstalledVersion; }
        public void Restart(UpdateJob job) { Calls.Add("restart"); OnRestart?.Invoke(); }
    }
}
