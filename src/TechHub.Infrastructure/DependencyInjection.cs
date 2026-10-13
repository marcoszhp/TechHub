using Microsoft.Extensions.DependencyInjection;
using TechHub.Core.Services;
using TechHub.Infrastructure.Actions;
using TechHub.Infrastructure.Automation;
using TechHub.Infrastructure.Dashboard;
using TechHub.Infrastructure.Diagnostics;
using TechHub.Infrastructure.Cleaning;
using TechHub.Infrastructure.Drivers;
using TechHub.Infrastructure.Network;
using TechHub.Infrastructure.Printers;
using TechHub.Infrastructure.Security;
using TechHub.Infrastructure.Software;
using TechHub.Infrastructure.Storage;
using TechHub.Infrastructure.Support;

namespace TechHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTechHubInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationPathsProvider>();
        services.AddSingleton<IApplicationPaths>(provider => provider.GetRequiredService<ApplicationPathsProvider>());
        services.AddSingleton<IPrivilegeService, WindowsPrivilegeService>();
        services.AddSingleton<IAuditStore, SqliteAuditStore>();
        services.AddSingleton<IRestorePointService, WindowsRestorePointService>();
        services.AddSingleton<IActionRunner, ActionRunner>();
        services.AddSingleton<IDashboardService, SystemMetricsService>();
        services.AddSingleton<IDiagnosticService, WindowsDiagnosticService>();
        services.AddSingleton<IDriverService, DriverService>();
        services.AddSingleton<ICleaningService, CleaningService>();
        services.AddSingleton<IPrinterService, PrinterService>();
        services.AddSingleton<INetworkConsentStore, NetworkConsentStore>();
        services.AddSingleton<INetworkScanner, NetworkScanner>();
        services.AddSingleton<ISecurityService, WindowsSecurityService>();
        services.AddSingleton<ITicketService, SqliteTicketService>();
        services.AddSingleton<IReportService, FileReportService>();
        services.AddSingleton<ISoftwareService, WindowsSoftwareService>();
        services.AddSingleton<IAutomationService, JsonAutomationService>();
        services.AddSingleton<IDiagnosticAssistant, OfflineDiagnosticAssistant>();
        services.AddSingleton<ISubnetCalculator, SubnetCalculator>();
        return services;
    }
}
