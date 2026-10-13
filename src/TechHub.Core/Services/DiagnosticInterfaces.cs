using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface IDiagnosticService
{
    Task<DiagnosticSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    ActionRequest CreateRepairRequest(DiagnosticRepairKind repair);
}
