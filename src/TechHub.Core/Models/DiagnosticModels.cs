namespace TechHub.Core.Models;

public sealed record DiagnosticDiskInfo(
    string Model,
    string Status,
    long SizeBytes,
    string? SerialNumber);

public sealed record DiagnosticEventInfo(
    DateTimeOffset? TimeCreated,
    string Provider,
    int Id,
    string Level,
    string Message);

public sealed record DiagnosticSnapshot(
    string OperatingSystem,
    string OsVersion,
    TimeSpan Uptime,
    IReadOnlyList<DiagnosticDiskInfo> Disks,
    IReadOnlyList<DiagnosticEventInfo> RecentCriticalEvents,
    IReadOnlyList<string> Recommendations,
    string? Error = null);

public enum DiagnosticRepairKind
{
    SystemFileChecker,
    DeploymentImageServicing
}
