namespace TechHub.Core.Models;

public enum CleaningMode { Safe, Deep }

public enum CleaningScope
{
    UserTemp, SystemTemp, WindowsUpdateDownloads, Prefetch, RecycleBin,
    LogsAndDumps, ChromeCache, EdgeCache, FirefoxCache
}

// Locations restringe a análise a arquivos exatos, sem ampliar os roots do catálogo.
public sealed record CleaningOptions(CleaningMode Mode, IReadOnlyList<CleaningScope> Scopes,
    IReadOnlyList<string>? Locations = null);

public sealed record CleaningItem(
    string Location, long Bytes, string Reason, CleaningScope Scope,
    DateTime LastWriteTimeUtc, DateTime CreationTimeUtc);

public sealed record CleaningIssue(string Location, string Message);

public sealed record CleaningPreview(
    Guid Id, CleaningMode Mode, IReadOnlyList<CleaningItem> Items,
    IReadOnlyList<CleaningIssue> Issues, long TotalBytes, bool RequiresAdministrator);
