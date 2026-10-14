using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Workspace;

public static class WorkspaceRegistration
{
    public static IServiceCollection AddTechHubWorkspace(this IServiceCollection services)
    {
        services.TryAddSingleton<IComputerInventoryService, ComputerInventoryService>();
        services.TryAddSingleton<IWorkspaceToolsService, WindowsWorkspaceToolsService>();
        services.TryAddSingleton<IWorkspaceClientStore, SqliteWorkspaceClientStore>();
        services.TryAddSingleton<IWorkspaceBackupService, WorkspaceBackupService>();
        return services;
    }
}
