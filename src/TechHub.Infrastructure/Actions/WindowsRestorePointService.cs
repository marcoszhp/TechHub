using TechHub.Core.Services;

namespace TechHub.Infrastructure.Actions;

public sealed class WindowsRestorePointService(IPowerShellExecutor powerShellExecutor) : IRestorePointService
{
    public async Task<bool> TryCreateAsync(string description, CancellationToken cancellationToken = default)
    {
        var safeDescription = description.Replace("'", "''", StringComparison.Ordinal);
        try
        {
            await powerShellExecutor.ExecuteAsync(
                $"Checkpoint-Computer -Description '{safeDescription}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop",
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
