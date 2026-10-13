namespace TechHub.Core.Models;

public sealed record DashboardSnapshot(
    int HealthScore,
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    long FreeDiskBytes,
    long TotalMemoryBytes,
    string OperatingSystem,
    bool IsAdministrator,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> Alerts);
