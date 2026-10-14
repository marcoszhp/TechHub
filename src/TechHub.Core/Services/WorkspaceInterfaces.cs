using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface IComputerInventoryService
{
    Task<ComputerInventory> GetAsync(CancellationToken cancellationToken = default);
}

public interface IWorkspaceToolsService
{
    IReadOnlyList<WorkspaceTool> Tools { get; }
    void Launch(string toolId);
    Task<DiagnosticCompletion> RunAsync(WorkspaceDiagnostic diagnostic, string? host,
        IProgress<string>? output = null, CancellationToken cancellationToken = default);
}

public interface IWorkspaceClientStore
{
    Task<IReadOnlyList<WorkspaceClient>> FindClientsAsync(string search = "", CancellationToken cancellationToken = default);
    Task<WorkspaceClient> SaveClientAsync(WorkspaceClient client, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceEquipment>> FindEquipmentAsync(long clientId, string search = "", CancellationToken cancellationToken = default);
    Task<WorkspaceEquipment> SaveEquipmentAsync(WorkspaceEquipment equipment, CancellationToken cancellationToken = default);
}

public interface IWorkspaceBackupService
{
    Task<WorkspaceBackupResult> ExportAsync(string zipPath, CancellationToken cancellationToken = default);
    Task<WorkspaceBackupResult> RestoreAsync(string zipPath, string emptyFolder, CancellationToken cancellationToken = default);
}
