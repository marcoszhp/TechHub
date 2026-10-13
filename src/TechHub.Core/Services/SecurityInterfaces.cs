using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface ISecurityService
{
    Task<SecuritySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    ActionRequest CreateActionRequest(SecurityActionKind action);
}
