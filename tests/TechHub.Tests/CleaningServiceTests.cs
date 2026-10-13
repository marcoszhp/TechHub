using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Infrastructure.Actions;
using TechHub.Infrastructure.Cleaning;
using TechHub.Infrastructure.Security;
using TechHub.Infrastructure.Storage;

namespace TechHub.Tests;

// Integração real: arquivos isolados em TEMP, ActionRunner e auditoria SQLite, sem mocks.
public sealed class CleaningServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "TechHub-CleaningTests-" + Guid.NewGuid().ToString("N"));
    private readonly CleaningService service;
    private readonly SqliteAuditStore audit;

    public CleaningServiceTests()
    {
        Directory.CreateDirectory(directory);
        var shell = new ProcessPowerShellExecutor();
        audit = new SqliteAuditStore(new TestPaths(directory));
        var runner = new ActionRunner(audit, new WindowsRestorePointService(shell), new WindowsPrivilegeService(), NullLogger<ActionRunner>.Instance);
        service = new CleaningService(runner, shell);
    }

    private string FileAt(string name, string contents = "conteúdo de teste")
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, contents);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));
        return path;
    }

    private Task<CleaningPreview> Analyze(params string[] files) => service.AnalyzeAsync(new(CleaningMode.Safe, [CleaningScope.UserTemp], files));
    private static ActionExecutionContext Consent(bool confirmed = true) => new(confirmed, false, "teste de limpeza");

    [Fact]
    public async Task PreviewDoesNotDeleteAndRunnerRequiresConfirmation()
    {
        var path = FileAt("preview.tmp");
        var preview = await Analyze(path);
        Assert.Single(preview.Items);
        Assert.Equal(new FileInfo(path).Length, preview.TotalBytes);
        Assert.True(File.Exists(path));
        var request = service.CreateActionRequest(preview);
        Assert.Equal(ActionRisk.Destructive, request.Risk);
        Assert.False(request.RequiresAdministrator);
        Assert.False((await request.ExecuteAsync(default, null)).Success);
        Assert.False((await service.ExecuteAsync(preview, Consent(false))).Success);
        Assert.True(File.Exists(path));
        Assert.Single(await audit.GetRecentAsync(10));
        Assert.True((await service.ExecuteAsync(preview, Consent())).Success);
        Assert.False(File.Exists(path));
        Assert.False((await service.ExecuteAsync(preview, Consent())).Success);
        Assert.Equal(3, (await audit.GetRecentAsync(10)).Count);
    }

    [Fact]
    public async Task DeletesOnlySnapshotAndReportsChangedFileAsPartialFailure()
    {
        var unchanged = FileAt("unchanged.tmp");
        var changed = FileAt("changed.tmp");
        var preview = await Analyze(unchanged, changed);
        File.AppendAllText(changed, "alteração após análise");
        var later = FileAt("later.tmp");
        var result = await service.ExecuteAsync(preview, Consent());
        Assert.False(result.Success);
        Assert.IsType<AggregateException>(result.Error);
        Assert.Contains("excluídos 1 de 2", result.Message);
        Assert.False(File.Exists(unchanged));
        Assert.True(File.Exists(changed));
        Assert.True(File.Exists(later));
    }

    [Fact]
    public async Task RejectsProtectedOutsideAndForgedPreview()
    {
        var executable = FileAt("protected.exe");
        var readOnly = FileAt("readonly.tmp");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechHub", "outside.tmp");
        var preview = await Analyze(executable, readOnly, outside);
        Assert.Empty(preview.Items);
        Assert.Equal(3, preview.Issues.Count);
        Assert.Throws<ArgumentException>(() => service.CreateActionRequest(preview with { TotalBytes = 1 }));
        Assert.True(File.Exists(executable));
        Assert.True(File.Exists(readOnly));
    }

    [Fact]
    public async Task LockedFileFailsSafelyAndCancellationPreservesCountsAndAudit()
    {
        var first = FileAt("first.tmp");
        var second = FileAt("second.tmp");
        var preview = await Analyze(first, second);
        using (var held = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await service.ExecuteAsync(preview, Consent());
            Assert.False(result.Success);
            Assert.True(File.Exists(first));
            Assert.False(File.Exists(second));
        }
        second = FileAt("second.tmp");
        preview = await Analyze(first, second);
        using var cancellation = new CancellationTokenSource();
        var cancelled = await service.ExecuteAsync(preview, Consent(), new CancelOnProgress(cancellation), cancellation.Token);
        Assert.False(cancelled.Success);
        Assert.Contains("excluídos 1 de 2", cancelled.Message);
        Assert.Equal(1, new[] { first, second }.Count(File.Exists));
        Assert.Equal(2, (await audit.GetRecentAsync(10)).Count);
    }

    [Fact]
    public async Task SafeModeExcludesRecentFilesAndRejectsDeepScopes()
    {
        var recent = FileAt("recent.tmp");
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow);
        Assert.Empty((await Analyze(recent)).Items);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync(new(CleaningMode.Safe, [CleaningScope.Prefetch])));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync(new(CleaningMode.Safe, [CleaningScope.UserTemp]), cancellation.Token));
    }

    public void Dispose()
    {
        // Apenas a pasta de teste criada por esta instância; não há limpeza de TEMP real.
        foreach (var file in Directory.GetFiles(directory)) File.SetAttributes(file, FileAttributes.Normal);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }

    private sealed class CancelOnProgress(CancellationTokenSource cancellation) : IProgress<ActionProgress>
    {
        public void Report(ActionProgress value)
        {
            if (value.Message.StartsWith("Excluídos ", StringComparison.Ordinal)) cancellation.Cancel();
        }
    }

    private sealed class TestPaths(string directory) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = new(StorageMode.Portable, directory, directory, directory,
            Path.Combine(directory, "audit.db"), Path.Combine(directory, "portable.flag"));
    }

    // Executor funcional para não adicionar referência ao projeto PowerShell no csproj dos testes.
    private sealed class ProcessPowerShellExecutor : IPowerShellExecutor
    {
        public async Task<string> ExecuteAsync(string script, CancellationToken cancellationToken = default)
        {
            var info = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
            using var process = Process.Start(info) ?? throw new IOException("Não foi possível iniciar PowerShell.");
            using var registration = cancellationToken.Register(() => { if (!process.HasExited) process.Kill(entireProcessTree: true); });
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) throw new IOException(await error);
            return await output;
        }
    }
}
