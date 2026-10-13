using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface IActionRunner
{
    Task<ActionPreview> PreviewAsync(ActionRequest request, CancellationToken cancellationToken = default);

    Task<ActionResult> ExecuteAsync(
        ActionRequest request,
        ActionExecutionContext context,
        IProgress<ActionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IAuditStore
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}

public interface IRestorePointService
{
    Task<bool> TryCreateAsync(string description, CancellationToken cancellationToken = default);
}

public interface IPrivilegeService
{
    bool IsAdministrator { get; }
}

public interface IApplicationPaths
{
    ApplicationPaths Current { get; }
}

public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IPrinterService
{
    Task<IReadOnlyList<PrinterInfo>> GetInventoryAsync(CancellationToken cancellationToken = default);

    ActionRequest CreateInstallByIpRequest(string ipAddress, string? printerName = null);
}

public interface INetworkConsentStore
{
    Task<bool> HasConsentAsync(CancellationToken cancellationToken = default);

    Task GrantConsentAsync(CancellationToken cancellationToken = default);
}

public interface INetworkScanner
{
    Task<IReadOnlyList<NetworkDeviceInfo>> ScanAsync(
        IProgress<ActionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IPowerShellExecutor
{
    Task<string> ExecuteAsync(string script, CancellationToken cancellationToken = default);
}
