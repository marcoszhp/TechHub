using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using TechHub.Core.Services;
using TechHub.Core.Updates;
using TechHub.Infrastructure.Updates;

namespace TechHub.Updater;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        string? directory = null;
        UpdateJob? job = null;
        bool validated = false;
        try
        {
            if (args.Length != 1 || Path.GetFileName(args[0]) != "job.json") return 2;
            directory = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
            if (!UpdatePolicy.SamePath(directory, AppContext.BaseDirectory)) return 2;
            using var directoryGuard = StagingDirectoryGuard.Acquire(directory);
            UpdatePolicy.RejectReparsePoints(args[0]);
            if (new FileInfo(args[0]).Length > 8192) return 2;
            job = JsonSerializer.Deserialize<UpdateJob>(await File.ReadAllTextAsync(args[0])) ?? throw new InvalidDataException("Plano ausente.");
            UpdatePolicy.Validate(job, directory);
            using var installationGuard = StagingDirectoryGuard.Acquire(job.InstallDirectory);
            validated = true;
            if (File.Exists(Path.Combine(directory, "aborted"))) return 1;
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Abra o TechHub sem executar como administrador para atualizar e reiniciar com segurança.");
            if (File.Exists(Path.Combine(directory, "result.json"))) throw new InvalidOperationException("Esta tentativa já foi concluída. Verifique novamente no aplicativo.");
            File.WriteAllText(Path.Combine(directory, "ready"), "ready");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using var runtime = new WindowsUpdateRuntime(directory);
            await new UpdateApplyEngine(runtime).ApplyAsync(job, directory, timeout.Token);
            return 0;
        }
        catch (Exception exception)
        {
            if (validated && directory is not null && File.Exists(Path.Combine(directory, "aborted"))) return 1;
            var message = $"Não foi possível concluir a atualização: {exception.Message}\n\nNenhum dado foi restaurado automaticamente. Abra o TechHub novamente ou use o instalador oficial. Detalhes e backup: {directory}";
            if (validated && directory is not null && job is not null)
            {
                try { await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new UpdateApplyResult(false, job.NextVersion, message, DateTimeOffset.UtcNow))); }
                catch { /* A failure to write must not hide the actionable on-screen error. */ }
            }
            MessageBox.Show(message, "TechHub — atualização interrompida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }
}

public sealed class WindowsUpdateRuntime(string? attemptDirectory = null, HttpClient? releaseClient = null) : IUpdateApplyRuntime, IDisposable
{
    private Mutex? _updateMarker;
    private MachineUpdateMarker? _machineMarker;
    private UpdateInstallMode _mode;
    public void Dispose() { _updateMarker?.Dispose(); _updateMarker = null; _machineMarker?.Dispose(); _machineMarker = null; }
    public async Task WaitForParentAsync(UpdateJob job, CancellationToken token)
    {
        _mode = job.Mode;
        Process process;
        try { process = Process.GetProcessById(job.ParentProcessId); }
        catch (ArgumentException) { return; }
        using (process)
        {
            if (process.HasExited) return;
            if (process.StartTime.ToUniversalTime().Ticks != job.ParentStartTicks ||
                !UpdatePolicy.SamePath(process.MainModule!.FileName, Path.Combine(job.InstallDirectory, "TechHub.App.exe")))
                throw new InvalidOperationException("A identidade do aplicativo mudou; atualização cancelada.");
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                while (!process.HasExited)
                {
                    if (attemptDirectory is not null && File.Exists(Path.Combine(attemptDirectory, "aborted")))
                        throw new OperationCanceledException("Tentativa de atualização cancelada.");
                    await Task.Delay(100, wait.Token);
                }
            }
            catch (OperationCanceledException) { throw new TimeoutException("O TechHub não fechou em 60 segundos. Feche-o e tente novamente; nenhum processo foi forçado a encerrar."); }
        }
    }

    public void AssertNoOtherInstances()
    {
        _updateMarker = new Mutex(false, "Local\\TechHub.Update.InProgress", out var created);
        if (!created) { _updateMarker.Dispose(); throw new InvalidOperationException("Outra atualização está em andamento."); }
        if (_mode == UpdateInstallMode.Machine)
        {
            _machineMarker = MachineUpdateMarker.Create(MachineUpdateMarker.Updating, out var newMarker);
            if (!newMarker) throw new InvalidOperationException("Outra sessão do Windows está atualizando o TechHub.");
            if (MachineUpdateMarker.Exists(MachineUpdateMarker.Running))
                throw new InvalidOperationException("Feche o TechHub em todas as sessões do Windows antes de atualizar a instalação de máquina.");
        }
        foreach (var process in Process.GetProcessesByName("TechHub.App"))
        {
            using (process)
                if (!process.HasExited) throw new InvalidOperationException("Há outra instância do TechHub aberta. Feche as demais janelas antes de atualizar.");
        }
    }

    public async Task VerifyReleaseAsync(UpdateJob job, CancellationToken token)
    {
        using var ownedClient = releaseClient is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) } : null;
        var client = releaseClient ?? ownedClient!;
        var uri = $"https://api.github.com/repos/marcoszhp/TechHub/releases/tags/v{job.NextVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("TechHub-Updater/1.3");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is Uri actual && actual.AbsoluteUri != uri)
            throw new InvalidDataException("Redirecionamento inesperado da release oficial.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var metadata = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (metadata.Length + count > 1024 * 1024) throw new InvalidDataException("Metadados da release excedem o limite.");
            metadata.Write(buffer, 0, count);
        }
        using var json = JsonDocument.Parse(metadata.ToArray());
        var root = json.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean() || root.GetProperty("tag_name").GetString() != "v" + job.NextVersion)
            throw new InvalidDataException("A release não é estável/publicada.");
        var matches = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == UpdatePolicy.PackageName(job)).ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("size").GetInt64() != job.SizeBytes ||
            !string.Equals(matches[0].GetProperty("digest").GetString(), "sha256:" + job.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A integridade não corresponde à release oficial do GitHub.");
    }

    public async Task<int> InstallAsync(UpdateJob job, string directory, CancellationToken token)
    {
        UpdateApplyEngine.EnsureAuthorized(directory);
        if (ReadInstalledVersion(job) != job.CurrentVersion)
            throw new InvalidOperationException("A versão instalada mudou desde a verificação. Abra o aplicativo e verifique novamente.");
        var start = CreateInstallerStartInfo(job, directory);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("O Windows não iniciou o instalador.");
        // Once installation starts, never kill it on cancellation/timeout; a partial installation is unsafe.
        await process.WaitForExitAsync(CancellationToken.None);
        return process.ExitCode;
    }

    public static ProcessStartInfo CreateInstallerStartInfo(UpdateJob job, string directory)
    {
        var info = new ProcessStartInfo(Path.Combine(directory, UpdatePolicy.PackageName(job)))
        {
            UseShellExecute = true,
            Verb = job.Mode == UpdateInstallMode.Machine ? "runas" : "open",
            WorkingDirectory = directory
        };
        foreach (var arg in new[] { "/SILENT", "/SP-", "/SUPPRESSMSGBOXES", "/NORESTART", "/RESTARTEXITCODE=3010",
            "/NOCLOSEAPPLICATIONS", "/NOFORCECLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/TECHHUBUPDATE",
            "/DIR=" + job.InstallDirectory, "/LOG=" + Path.Combine(directory, "installer.log") }) info.ArgumentList.Add(arg);
        return info;
    }

    public string ReadInstalledVersion(UpdateJob job)
    {
        var version = FileVersionInfo.GetVersionInfo(Path.Combine(job.InstallDirectory, "TechHub.App.exe"));
        return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
    }

    public void Restart(UpdateJob job)
    {
        Dispose();
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(job.InstallDirectory, "TechHub.App.exe"))
            { UseShellExecute = false, WorkingDirectory = job.InstallDirectory }) ?? throw new InvalidOperationException("Não foi possível reiniciar. Abra o TechHub pelo atalho.");
    }
}
