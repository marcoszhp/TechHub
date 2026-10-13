using Microsoft.Extensions.Logging;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Actions;

public sealed class ActionRunner(
    IAuditStore auditStore,
    IRestorePointService restorePointService,
    IPrivilegeService privilegeService,
    ILogger<ActionRunner> logger) : IActionRunner
{
    public Task<ActionPreview> PreviewAsync(ActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ActionPreview(
            request.Id,
            request.Title,
            request.Description,
            request.Risk,
            request.RequiresAdministrator,
            request.AffectedResources));
    }

    public async Task<ActionResult> ExecuteAsync(
        ActionRequest request,
        ActionExecutionContext context,
        IProgress<ActionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = DateTimeOffset.UtcNow;
        progress?.Report(new ActionProgress($"Preparando: {request.Title}", 0));

        if (request.RequiresAdministrator && !privilegeService.IsAdministrator)
        {
            return await RecordAsync(request, context, startedAt,
                ActionResult.Failed("Esta ação exige privilégios de administrador."), cancellationToken).ConfigureAwait(false);
        }

        if (request.Risk != ActionRisk.ReadOnly && !context.Confirmed)
        {
            return await RecordAsync(request, context, startedAt,
                ActionResult.Failed("Ação bloqueada: a confirmação explícita é obrigatória."), cancellationToken).ConfigureAwait(false);
        }

        if (context.CreateRestorePoint && request.Risk is ActionRisk.Elevated or ActionRisk.Destructive)
        {
            progress?.Report(new ActionProgress("Criando ponto de restauração", 10));
            if (!await restorePointService.TryCreateAsync($"TechHub - {request.Title}", cancellationToken).ConfigureAwait(false))
            {
                return await RecordAsync(request, context, startedAt,
                    ActionResult.Failed("A ação foi cancelada porque o ponto de restauração não pôde ser criado."), cancellationToken).ConfigureAwait(false);
            }
        }

        try
        {
            var result = await request.ExecuteAsync(cancellationToken, progress).ConfigureAwait(false);
            return await RecordAsync(request, context, startedAt, result, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var result = ActionResult.Failed("Ação cancelada pelo usuário.");
            return await RecordAsync(request, context, startedAt, result, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha na ação {ActionId}", request.Id);
            var result = ActionResult.Failed("A ação falhou. Consulte o log para obter detalhes.", exception);
            return await RecordAsync(request, context, startedAt, result, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<ActionResult> RecordAsync(
        ActionRequest request,
        ActionExecutionContext context,
        DateTimeOffset startedAt,
        ActionResult result,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        await auditStore.AppendAsync(new AuditEntry(
            request.Id,
            request.Title,
            context.Actor,
            context.Confirmed,
            result.Success,
            result.Message,
            startedAt,
            completedAt,
            result.Error?.ToString()), cancellationToken).ConfigureAwait(false);
        return result with { Duration = completedAt - startedAt };
    }
}
