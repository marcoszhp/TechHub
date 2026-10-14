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
        if (cpu is null) alerts.Add("CPU: medição indisponível nesta consulta.");
        if (memory.UsagePercent is null) alerts.Add("Memória: medição indisponível nesta consulta.");
        if (cpu >= 90) alerts.Add("Uso de CPU muito alto");
        if (memory.UsagePercent >= 90) alerts.Add("Memória disponível baixa");
        if (diskUsage >= 90) alerts.Add("Pouco espaço livre no disco do sistema");

        int? score = cpu is null || memory.UsagePercent is null ? null : Math.Clamp(100 - alerts.Count * 15 - (diskUsage >= 80 ? 10 : 0), 0, 100);
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

    private static double? ReadAverageWmiPercentage(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(8);
            var values = searcher.Get().Cast<ManagementObject>()
                .Where(item => item[property] is not null)
                .Select(item => Convert.ToDouble(item[property], System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            return values.Length == 0 ? null : Math.Round(values.Average(), 1);
        }
        catch
        {
            return null;
        }
    }

    private static (double? UsagePercent, long TotalBytes) ReadMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            searcher.Options.Timeout = TimeSpan.FromSeconds(8);
            var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            var totalKb = Convert.ToInt64(item?["TotalVisibleMemorySize"] ?? 0);
            var freeKb = Convert.ToInt64(item?["FreePhysicalMemory"] ?? 0);
            var totalBytes = totalKb * 1024L;
            double? usage = totalKb == 0 ? null : Math.Round((1d - (double)freeKb / totalKb) * 100d, 1);
            return (usage, totalBytes);
        }
        catch
        {
            return (null, 0);
        }
    }
}
