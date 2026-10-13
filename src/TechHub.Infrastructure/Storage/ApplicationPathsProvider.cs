using System.IO;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Storage;

public sealed class ApplicationPathsProvider : IApplicationPaths
{
    public ApplicationPaths Current { get; }

    public ApplicationPathsProvider()
    {
        var executableDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var portableFlagPath = Path.Combine(executableDirectory, "portable.flag");
        var isPortable = File.Exists(portableFlagPath);
        var rootDirectory = isPortable
            ? executableDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechHub");
        var dataDirectory = Path.Combine(rootDirectory, "Data");
        var logsDirectory = Path.Combine(dataDirectory, "Logs");

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(logsDirectory);

        Current = new ApplicationPaths(
            isPortable ? StorageMode.Portable : StorageMode.Installed,
            rootDirectory,
            dataDirectory,
            logsDirectory,
            Path.Combine(dataDirectory, "techhub.db"),
            portableFlagPath);
    }
}
