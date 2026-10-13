using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface ICleaningService
{
    Task<CleaningPreview> AnalyzeAsync(CleaningOptions options, CancellationToken cancellationToken = default);

    // A prévia pertence à instância que a analisou; alterações ou reconstruções são recusadas.
    ActionRequest CreateActionRequest(CleaningPreview preview);

    Task<ActionResult> ExecuteAsync(CleaningPreview preview, ActionExecutionContext context,
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default);
}
