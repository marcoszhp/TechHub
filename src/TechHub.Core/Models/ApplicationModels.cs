namespace TechHub.Core.Models;

public enum StorageMode
{
    Installed,
    Portable
}

public sealed record ApplicationPaths(
    StorageMode Mode,
    string RootDirectory,
    string DataDirectory,
    string LogsDirectory,
    string DatabasePath,
    string PortableFlagPath);

public sealed record ModuleDefinition(string Id, string Title, string Description);
