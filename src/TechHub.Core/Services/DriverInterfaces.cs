using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface IDriverService
{
    Task<DriverInventoryResult> GetInventoryAsync(CancellationToken cancellationToken = default);
    Task<DriverVerificationResult> VerifyOfflineDriverAsync(string infPath, CancellationToken cancellationToken = default);
    ActionRequest CreateBackupRequest(string publishedInfName, string destinationDirectory);
    Task<ActionRequest> CreateOfflineInstallRequestAsync(string infPath, CancellationToken cancellationToken = default);

    // Encaminha exclusivamente ao IActionRunner e habilita o callback do request
    // somente durante essa chamada. Invocar o callback diretamente é bloqueado.
    Task<ActionResult> ExecuteAsync(ActionRequest request, ActionExecutionContext context,
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default);
}
