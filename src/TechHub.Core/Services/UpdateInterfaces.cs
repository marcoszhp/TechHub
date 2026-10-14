namespace TechHub.Core.Services;

public sealed record UpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? DownloadUrl,
    string Message);

public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default);
}
