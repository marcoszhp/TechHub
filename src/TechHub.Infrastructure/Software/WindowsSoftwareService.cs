using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Software;

public sealed class WindowsSoftwareService : ISoftwareService
{
    public Task<IReadOnlyList<SoftwareInfo>> GetInventoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var result = new Dictionary<string, SoftwareInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var app = uninstall.OpenSubKey(name);
                        if (app is null) continue;
                        var displayName = app!.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        var version = app!.GetValue("DisplayVersion") as string ?? "Desconhecida";
                        var publisher = app!.GetValue("Publisher") as string ?? "Desconhecido";
                        result[$"{displayName}|{version}"] = new SoftwareInfo(displayName, version, publisher, app!.GetValue("InstallLocation") as string, $"Registro {hive}/{view}");
                    }
                }
                catch (SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }
            return (IReadOnlyList<SoftwareInfo>)result.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, cancellationToken);

    public async Task<SoftwareUpgradeSummary> GetUpgradeSummaryAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunWingetAsync("upgrade --include-unknown --accept-source-agreements", cancellationToken).ConfigureAwait(false);
        var packages = result.output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("  ", StringComparison.Ordinal) && !line.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
            .Take(100).ToArray();
        return new SoftwareUpgradeSummary(packages.Length, result.exitCode == 0 ? $"{packages.Length} linha(s) de atualização retornadas pelo winget." : "Não foi possível consultar o winget.", packages);
    }

    public ActionRequest CreateUpgradeAllRequest() => new(
        "software.winget-upgrade-all",
        "Atualizar aplicativos com winget",
        "Executa winget upgrade --all. Cada pacote pode ter efeitos próprios; revise a prévia antes de continuar.",
        ActionRisk.Elevated,
        false,
        ["winget", "Todos os aplicativos elegíveis"],
        async (cancellationToken, progress) =>
        {
            progress?.Report(new ActionProgress("Executando atualizações do winget...", 10));
            var result = await RunWingetAsync("upgrade --all --silent --accept-package-agreements --accept-source-agreements", cancellationToken).ConfigureAwait(false);
            progress?.Report(new ActionProgress("Atualização concluída.", 100));
            return new ActionResult(result.exitCode == 0, result.exitCode == 0 ? "Atualizações concluídas." : $"winget terminou com código {result.exitCode}.\n{result.output}", TimeSpan.Zero);
        });

    private static async Task<(int exitCode, string output)> RunWingetAsync(string arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("winget") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("winget não está disponível neste Windows.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = (await stdout.ConfigureAwait(false) + Environment.NewLine + await stderr.ConfigureAwait(false)).Trim();
        if (output.Length > 12000) output = output[..12000];
        return (process.ExitCode, output);
    }
}
