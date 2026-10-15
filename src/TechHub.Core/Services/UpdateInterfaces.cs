namespace TechHub.Core.Services;

public enum UpdateCheckStatus { UpToDate, Available, Unsupported, Failed }
public enum UpdateInstallMode { User, Machine, Portable, Unknown }

public sealed record UpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? DownloadUrl,
    string Message,
    UpdateCheckStatus Status = UpdateCheckStatus.UpToDate,
    UpdateInstallMode InstallMode = UpdateInstallMode.Unknown,
    string? Sha256 = null,
    long SizeBytes = 0,
    string? ReleaseNotes = null);

public sealed record UpdateProgress(long BytesReceived, long TotalBytes)
{
    public double Percent => TotalBytes > 0 ? Math.Clamp(100d * BytesReceived / TotalBytes, 0, 100) : 0;
}

public sealed record DownloadedUpdate(UpdateInfo Update, string InstallerPath);

public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default);

    Task<DownloadedUpdate> DownloadAsync(UpdateInfo update, IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IUpdateInstaller
{
    string? LastResultMessage { get; }
    Task<bool> ConfirmAsync(UpdateInfo update);
    Task ApplyAndRestartAsync(DownloadedUpdate update, CancellationToken cancellationToken = default);
}
