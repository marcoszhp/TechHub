using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TechHub.App.ViewModels;
using TechHub.Core.Services;
using TechHub.Core.Updates;
using TechHub.Infrastructure.Updates;

namespace TechHub.App.Services;

public sealed class DesktopUpdateInstaller(IServiceProvider services, IWorkspaceBackupService backup, IApplicationPaths paths) : IUpdateInstaller
{
    public string? LastResultMessage { get; } = paths.Current.Mode == TechHub.Core.Models.StorageMode.Portable ? null : ReadPreviousResult();
    private bool _applying;

    public Task<bool> ConfirmAsync(UpdateInfo update)
    {
        AssertIdle();
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Feche o TechHub e abra normalmente (sem Executar como administrador) antes de atualizar.");
        return Task.FromResult(MessageBox.Show(Application.Current.MainWindow,
            $"Atualizar de {update.CurrentVersion} para {update.LatestVersion}?\n\nSalve os formulários e chamados antes de continuar. O TechHub fará um backup dos dados salvos, fechará e abrirá novamente após a atualização.\n\n" +
            (update.InstallMode == UpdateInstallMode.Machine ? "A instalação para todos os usuários pedirá autorização do Windows.\n\n" : "Não é necessário executar como administrador.\n\n") +
            "Você pode cancelar o download. Depois de iniciar a instalação, aguarde sua conclusão.",
            "TechHub — atualizar e reiniciar", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes);
    }

    public async Task ApplyAndRestartAsync(DownloadedUpdate downloaded, CancellationToken cancellationToken = default)
    {
        if (_applying) throw new InvalidOperationException("Já há uma atualização em preparação.");
        AssertIdle();
        var app = Application.Current as App ?? throw new InvalidOperationException("Aplicativo indisponível.");
        var window = app.MainWindow;
        if (MessageBox.Show(window,
            "O download foi verificado. Salve qualquer alteração feita enquanto ele era baixado.\n\nFechar, atualizar e reiniciar o TechHub agora?",
            "TechHub — pronto para atualizar", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            throw new OperationCanceledException("Reinício adiado pelo usuário.");
        AssertIdle();
        _applying = true;
        window.IsEnabled = false;
        string? attemptDirectory = null;
        try
        {
            using var parent = Process.GetCurrentProcess();
            var update = downloaded.Update;
            var job = new UpdateJob(parent.Id, parent.StartTime.ToUniversalTime().Ticks, AppContext.BaseDirectory,
                update.InstallMode, update.CurrentVersion, update.LatestVersion, update.Sha256 ?? "", update.SizeBytes);
            var directory = Path.GetDirectoryName(Path.GetFullPath(downloaded.InstallerPath))!;
            using var directoryGuard = StagingDirectoryGuard.Acquire(directory);
            using var installationGuard = StagingDirectoryGuard.Acquire(job.InstallDirectory);
            UpdatePolicy.Validate(job, directory);
            attemptDirectory = directory;
            if (!UpdatePolicy.SamePath(downloaded.InstallerPath, Path.Combine(directory, UpdatePolicy.PackageName(job))))
                throw new InvalidDataException("Nome do instalador inesperado.");
            await using (var file = new FileStream(downloaded.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await UpdatePolicy.VerifyPackageAsync(file, job, cancellationToken);
            var helper = Path.Combine(AppContext.BaseDirectory, "TechHub.Updater.exe");
            UpdatePolicy.RejectReparsePoints(helper);
            if (!File.Exists(helper)) throw new FileNotFoundException("O atualizador não está instalado. Instale a versão completa uma vez pelo site oficial.");
            // Back up through SQLite's online snapshot, not by copying an active database.
            if (File.Exists(paths.Current.DatabasePath))
                await backup.ExportAsync(Path.Combine(directory, "backup-before-update.zip"), cancellationToken);
            else if (Directory.EnumerateFiles(paths.Current.DataDirectory, "*", SearchOption.TopDirectoryOnly).Any())
                throw new InvalidOperationException("Há arquivos de dados, mas o banco não foi localizado. Faça um backup manual antes de atualizar.");
            else
                await File.WriteAllTextAsync(Path.Combine(directory, "no-data-before-update.txt"), "Nenhum banco ou configuração existia antes desta atualização.", cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(helper, Path.Combine(directory, "TechHub.Updater.exe"), overwrite: false);
            var jobPath = Path.Combine(directory, "job.json");
            await using (var file = new FileStream(jobPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(file, job, cancellationToken: cancellationToken);
            var start = new ProcessStartInfo(Path.Combine(directory, "TechHub.Updater.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
            start.ArgumentList.Add(jobPath);
            using var updater = Process.Start(start) ?? throw new InvalidOperationException("O atualizador não iniciou.");
            // After handoff do not cancel and leave a helper orphaned waiting for a future close.
            var ready = Path.Combine(directory, "ready");
            var watch = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (updater.HasExited || watch.Elapsed > TimeSpan.FromSeconds(15))
                    throw new InvalidOperationException("O atualizador não ficou pronto. O aplicativo permanecerá aberto; consulte a pasta Updates.");
                await Task.Delay(100);
            }
            await File.WriteAllTextAsync(Path.Combine(UpdatePolicy.StagingRoot, "last-attempt.txt"), Path.GetFileName(directory));
            await File.WriteAllTextAsync(Path.Combine(directory, "apply-authorized"), "User confirmed; ready to close.");
            await app.ShutdownForUpdateAsync();
        }
        catch
        {
            if (attemptDirectory is not null)
            {
                try
                {
                    using var guard = StagingDirectoryGuard.Acquire(attemptDirectory);
                    await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "aborted"), "Handoff failed. This attempt must not apply.");
                }
                catch (Exception failure) { Serilog.Log.Warning(failure, "Falha ao registrar cancelamento da atualização"); }
            }
            throw;
        }
        finally { _applying = false; window.IsEnabled = true; }
    }

    private void AssertIdle()
    {
        if (services.GetRequiredService<MainViewModel>().HasActiveWorkForUpdate())
            throw new InvalidOperationException("Aguarde as operações em andamento terminarem antes de atualizar.");
        foreach (var process in Process.GetProcessesByName("TechHub.App"))
        {
            using (process)
                if (process.Id != Environment.ProcessId && !process.HasExited)
                    throw new InvalidOperationException("Feche as outras janelas do TechHub antes de atualizar.");
        }
    }

    private static string? ReadPreviousResult()
    {
        try
        {
            var pointer = Path.Combine(UpdatePolicy.StagingRoot, "last-attempt.txt");
            UpdatePolicy.RejectReparsePoints(pointer);
            if (!File.Exists(pointer) || new FileInfo(pointer).Length > 64) return null;
            var name = File.ReadAllText(pointer).Trim();
            if (!Guid.TryParseExact(name, "N", out _)) return null;
            var resultPath = Path.Combine(UpdatePolicy.StagingRoot, name, "result.json");
            UpdatePolicy.RejectReparsePoints(resultPath);
            if (!File.Exists(resultPath)) return "A última atualização não foi confirmada. Consulte a pasta Updates antes de tentar novamente.";
            if (new FileInfo(resultPath).Length > 16384) return null;
            return JsonSerializer.Deserialize<UpdateApplyResult>(File.ReadAllText(resultPath))?.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
