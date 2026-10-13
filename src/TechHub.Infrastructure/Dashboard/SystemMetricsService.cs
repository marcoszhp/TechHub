using System.IO;
using System.Management;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Dashboard;

public sealed class SystemMetricsService(IPrivilegeService privilegeService) : IDashboardService
{
    public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private DashboardSnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpu = ReadAverageWmiPercentage("SELECT LoadPercentage FROM Win32_Processor", "LoadPercentage");
        var memory = ReadMemory();
        var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        var diskUsage = systemDrive.TotalSize == 0 ? 0 : (1d - (double)systemDrive.AvailableFreeSpace / systemDrive.TotalSize) * 100d;
        var alerts = new List<string>();
        if (cpu >= 90) alerts.Add("Uso de CPU muito alto");
        if (memory.UsagePercent >= 90) alerts.Add("Memória disponível baixa");
        if (diskUsage >= 90) alerts.Add("Pouco espaço livre no disco do sistema");

        var score = Math.Clamp(100 - alerts.Count * 15 - (diskUsage >= 80 ? 10 : 0), 0, 100);
        return new DashboardSnapshot(
            score,
            cpu,
            memory.UsagePercent,
            diskUsage,
            systemDrive.AvailableFreeSpace,
            memory.TotalBytes,
            Environment.OSVersion.VersionString,
            privilegeService.IsAdministrator,
            DateTimeOffset.Now,
            alerts);
    }

    private static double ReadAverageWmiPercentage(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            var values = searcher.Get().Cast<ManagementObject>()
                .Select(item => Convert.ToDouble(item[property] ?? 0, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            return values.Length == 0 ? 0 : Math.Round(values.Average(), 1);
        }
        catch
        {
            return 0;
        }
    }

    private static (double UsagePercent, long TotalBytes) ReadMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            var totalKb = Convert.ToInt64(item?["TotalVisibleMemorySize"] ?? 0);
            var freeKb = Convert.ToInt64(item?["FreePhysicalMemory"] ?? 0);
            var totalBytes = totalKb * 1024L;
            var usage = totalKb == 0 ? 0 : (1d - (double)freeKb / totalKb) * 100d;
            return (Math.Round(usage, 1), totalBytes);
        }
        catch
        {
            return (0, 0);
        }
    }
}
