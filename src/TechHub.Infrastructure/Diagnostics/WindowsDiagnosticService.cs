using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Diagnostics;

public sealed class WindowsDiagnosticService(
    IPowerShellExecutor powerShell,
    ILogger<WindowsDiagnosticService> logger) : IDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<DiagnosticSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await powerShell.ExecuteAsync("""
                $os = Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 10
                $disks = @(Get-CimInstance Win32_DiskDrive -OperationTimeoutSec 10 -ErrorAction SilentlyContinue | Select-Object Model,Status,Size,SerialNumber)
                $events = @(Get-WinEvent -FilterHashtable @{LogName='System'; Level=1,2} -MaxEvents 12 -ErrorAction SilentlyContinue | ForEach-Object {
                    [pscustomobject]@{ TimeCreated = $_.TimeCreated; Provider = $_.ProviderName; Id = $_.Id; Level = $_.LevelDisplayName; Message = $_.Message }
                })
                [pscustomobject]@{
                    OperatingSystem = $os.Caption
                    OsVersion = $os.Version
                    UptimeHours = ((Get-Date) - $os.LastBootUpTime).TotalHours
                    Disks = $disks
                    RecentCriticalEvents = $events
                } | ConvertTo-Json -Depth 6 -Compress
                """, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Deserialize<DiagnosticPayload>(json, JsonOptions);
            if (payload is null)
            {
                return new DiagnosticSnapshot("Desconhecido", "Desconhecida", TimeSpan.Zero, [], [],
                    ["Não foi possível interpretar o diagnóstico."], "Resposta inválida do PowerShell.");
            }

            var disks = Normalize(payload.Disks);
            var events = Normalize(payload.RecentCriticalEvents);
            var recommendations = new List<string>();
            if (disks.Any(d => !string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase)))
                recommendations.Add("Há um disco com status diferente de OK; faça backup antes de reparos.");
            if (events.Count > 0)
                recommendations.Add("Existem eventos críticos recentes no log System; revise os detalhes antes de executar reparos.");
            if (recommendations.Count == 0)
                recommendations.Add("Nenhum alerta crítico foi encontrado nesta coleta básica.");

            cancellationToken.ThrowIfCancellationRequested();
            return new DiagnosticSnapshot(
                payload.OperatingSystem ?? "Desconhecido",
                payload.OsVersion ?? "Desconhecida",
                TimeSpan.FromHours(Math.Max(0, payload.UptimeHours)),
                disks,
                events,
                recommendations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao coletar diagnóstico do Windows.");
            return new DiagnosticSnapshot("Desconhecido", "Desconhecida", TimeSpan.Zero, [], [],
                ["A coleta de diagnóstico falhou; consulte o log técnico."], exception.Message);
        }
    }

    public ActionRequest CreateRepairRequest(DiagnosticRepairKind repair) => repair switch
    {
        DiagnosticRepairKind.SystemFileChecker => CreateCommandRequest(
            "diagnostics.sfc",
            "Verificar arquivos protegidos do Windows (SFC)",
            "Executa sfc.exe /scannow e pode reparar arquivos protegidos do sistema.",
            "sfc.exe",
            "/scannow"),
        DiagnosticRepairKind.DeploymentImageServicing => CreateCommandRequest(
            "diagnostics.dism",
            "Reparar a imagem do Windows (DISM)",
            "Executa DISM /Online /Cleanup-Image /RestoreHealth e pode alterar componentes do Windows.",
            "dism.exe",
            "/Online /Cleanup-Image /RestoreHealth"),
        _ => throw new ArgumentOutOfRangeException(nameof(repair), repair, "Reparo não suportado.")
    };

    private ActionRequest CreateCommandRequest(string id, string title, string description, string executable, string arguments) =>
        new(
            id,
            title,
            description,
            ActionRisk.Elevated,
            true,
            [executable, arguments, "Componentes do Windows"],
            async (cancellationToken, progress) =>
            {
                progress?.Report(new ActionProgress($"Executando {executable}...", 10));
                var script = $"$p = Start-Process -FilePath '{executable}' -ArgumentList '{arguments}' -Wait -PassThru -NoNewWindow; if ($p.ExitCode -ne 0) {{ throw '{executable} terminou com código ' + $p.ExitCode }}; 'Comando concluído com sucesso.'";
                var output = await powerShell.ExecuteAsync(script, cancellationToken).ConfigureAwait(false);
                progress?.Report(new ActionProgress("Reparo concluído.", 100));
                return new ActionResult(true, output, TimeSpan.Zero);
            });

    private static IReadOnlyList<DiagnosticDiskInfo> Normalize(IReadOnlyList<DiagnosticDiskPayload>? source) =>
        source?.Select(d => new DiagnosticDiskInfo(d.Model ?? "Desconhecido", d.Status ?? "Desconhecido", d.Size, d.SerialNumber)).ToList()
        ?? [];

    private static IReadOnlyList<DiagnosticEventInfo> Normalize(IReadOnlyList<DiagnosticEventPayload>? source) =>
        source?.Select(e => new DiagnosticEventInfo(e.TimeCreated, e.Provider ?? "Desconhecido", e.Id, e.Level ?? "Desconhecido", e.Message ?? "")).ToList()
        ?? [];

    private sealed record DiagnosticPayload(
        string? OperatingSystem,
        string? OsVersion,
        double UptimeHours,
        List<DiagnosticDiskPayload>? Disks,
        List<DiagnosticEventPayload>? RecentCriticalEvents);

    private sealed record DiagnosticDiskPayload(string? Model, string? Status, long Size, string? SerialNumber);

    private sealed record DiagnosticEventPayload(DateTimeOffset? TimeCreated, string? Provider, int Id, string? Level, string? Message);
}
