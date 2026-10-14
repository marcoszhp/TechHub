using Microsoft.Extensions.Logging.Abstractions;
using TechHub.Core.Services;
using TechHub.Infrastructure.Diagnostics;
using TechHub.Infrastructure.Security;

namespace TechHub.Tests;

public sealed class Phase2ServiceTests
{
    [Fact]
    public async Task SecurityService_MissingMeasurementsRemainUnknown()
    {
        var service = new WindowsSecurityService(new StubPowerShellExecutor("{}"), NullLogger<WindowsSecurityService>.Instance);
        var snapshot = await service.GetSnapshotAsync();
        Assert.Null(snapshot.DefenderEnabled);
        Assert.Null(snapshot.FirewallEnabled);
        Assert.Null(snapshot.SecureBootEnabled);
        Assert.False(snapshot.IsHealthy);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("não verificados"));
        Assert.DoesNotContain(snapshot.Warnings, warning => warning.Contains("desativados"));
    }

    [Fact]
    public async Task SecurityService_InvalidPayloadDoesNotReportDisabledControls()
    {
        var service = new WindowsSecurityService(new StubPowerShellExecutor("not-json"), NullLogger<WindowsSecurityService>.Instance);
        var snapshot = await service.GetSnapshotAsync();
        Assert.Null(snapshot.DefenderEnabled);
        Assert.Null(snapshot.BitLockerEnabled);
        Assert.NotNull(snapshot.Error);
    }

    [Fact]
    public async Task SecurityService_ConvertsWindowsPayloadIntoWarnings()
    {
        var executor = new StubPowerShellExecutor("""
            {"DefenderEnabled":true,"RealTimeProtectionEnabled":false,"FirewallEnabled":true,"BitLockerEnabled":true,"TpmPresent":true,"SecureBootEnabled":true,"LocalAdministratorCount":2}
            """);
        var service = new WindowsSecurityService(executor, NullLogger<WindowsSecurityService>.Instance);

        var snapshot = await service.GetSnapshotAsync();

        Assert.True(snapshot.DefenderEnabled);
        Assert.False(snapshot.RealTimeProtectionEnabled);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("tempo real", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, snapshot.LocalAdministratorCount);
    }

    [Fact]
    public async Task DiagnosticService_ParsesDiskAndEventData()
    {
        var executor = new StubPowerShellExecutor("""
            {"OperatingSystem":"Windows Test","OsVersion":"10.0","UptimeHours":4.5,"Disks":[{"Model":"Disk A","Status":"OK","Size":100,"SerialNumber":"ABC"}],"RecentCriticalEvents":[{"TimeCreated":"2026-10-13T10:00:00Z","Provider":"Test","Id":100,"Level":"Error","Message":"Falha"}]}
            """);
        var service = new WindowsDiagnosticService(executor, NullLogger<WindowsDiagnosticService>.Instance);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Windows Test", snapshot.OperatingSystem);
        Assert.Single(snapshot.Disks);
        Assert.Equal("Disk A", snapshot.Disks[0].Model);
        Assert.Single(snapshot.RecentCriticalEvents);
        Assert.NotEmpty(snapshot.Recommendations);
    }

    private sealed class StubPowerShellExecutor(string output) : IPowerShellExecutor
    {
        public Task<string> ExecuteAsync(string script, CancellationToken cancellationToken = default) =>
            Task.FromResult(output);
    }
}
