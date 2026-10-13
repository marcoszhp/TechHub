using System.IO;
using System.Text.Json;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Automation;

public sealed class JsonAutomationService(
    IApplicationPaths paths,
    IActionRunner actionRunner,
    IDashboardService dashboardService,
    IReportService reportService) : IAutomationService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string FilePath => Path.Combine(paths.Current.DataDirectory, "automations.json");

    public async Task<IReadOnlyList<AutomationPlaybook>> GetPlaybooksAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureFileAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<List<AutomationPlaybook>>(stream, jsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(AutomationPlaybook playbook, CancellationToken cancellationToken = default)
    {
        Validate(playbook);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            current.RemoveAll(item => item.Id.Equals(playbook.Id, StringComparison.OrdinalIgnoreCase));
            current.Add(playbook);
            Directory.CreateDirectory(paths.Current.DataDirectory);
            await using var stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(stream, current, jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<AutomationRunResult> RunAsync(string playbookId, bool confirmed, string actor, CancellationToken cancellationToken = default)
    {
        var playbook = (await GetPlaybooksAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.Id.Equals(playbookId, StringComparison.OrdinalIgnoreCase) && item.Enabled)
            ?? throw new InvalidOperationException("Playbook não encontrado ou desativado.");
        var outputs = new List<string>();
        foreach (var step in playbook.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = CreateRequest(step);
            if (step.RequiresConfirmation && !confirmed) return new AutomationRunResult(false, $"O playbook exige confirmação para a etapa: {step.Name}.", outputs);
            var result = await actionRunner.ExecuteAsync(request, new ActionExecutionContext(confirmed, false, actor), cancellationToken: cancellationToken).ConfigureAwait(false);
            outputs.Add(result.Message);
            if (!result.Success) return new AutomationRunResult(false, $"Playbook interrompido na etapa {step.Name}: {result.Message}", outputs);
        }
        return new AutomationRunResult(true, $"Playbook '{playbook.Name}' concluído.", outputs);
    }

    private ActionRequest CreateRequest(AutomationStep step) => step.Action switch
    {
        AutomationActionKind.CollectDashboard => new ActionRequest($"automation.{step.Id}", step.Name, "Coletar métricas locais do Dashboard.", ActionRisk.ReadOnly, false, ["Dashboard local"], async (ct, _) =>
        {
            var snapshot = await dashboardService.GetSnapshotAsync(ct).ConfigureAwait(false);
            return new ActionResult(true, $"Saúde {snapshot.HealthScore}/100; alertas: {snapshot.Alerts.Count}.", TimeSpan.Zero);
        }),
        AutomationActionKind.ExportTicketsCsv => new ActionRequest($"automation.{step.Id}", step.Name, "Exportar tickets para o diretório Data/Reports.", ActionRisk.Safe, false, ["Data/Reports"], async (ct, _) =>
        {
            var output = Path.Combine(paths.Current.DataDirectory, "Reports", $"tickets-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
            return new ActionResult(true, $"Relatório criado em {await reportService.ExportTicketsCsvAsync(output, ct).ConfigureAwait(false)}.", TimeSpan.Zero);
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(step.Action), step.Action, "Ação de automação não suportada.")
    };

    private async Task EnsureFileAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(FilePath)) return;
        var defaults = new List<AutomationPlaybook>
        {
            new("health-check", "Verificação de saúde", "Coleta métricas locais sem alterar o sistema.", [new("health", "Coletar Dashboard", AutomationActionKind.CollectDashboard, false)]),
            new("ticket-report", "Relatório de tickets", "Gera um relatório local de chamados.", [new("tickets", "Exportar tickets", AutomationActionKind.ExportTicketsCsv, true)])
        };
        Directory.CreateDirectory(paths.Current.DataDirectory);
        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, defaults, jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<AutomationPlaybook>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        await EnsureFileAsync(cancellationToken).ConfigureAwait(false);
        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<List<AutomationPlaybook>>(stream, jsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
    }

    private static void Validate(AutomationPlaybook playbook)
    {
        if (string.IsNullOrWhiteSpace(playbook.Id) || string.IsNullOrWhiteSpace(playbook.Name) || playbook.Steps.Count == 0) throw new ArgumentException("Playbook precisa de identificador, nome e ao menos uma etapa.");
        if (playbook.Steps.Any(step => string.IsNullOrWhiteSpace(step.Id) || string.IsNullOrWhiteSpace(step.Name))) throw new ArgumentException("Toda etapa precisa de identificador e nome.");
    }
}
