namespace TechHub.Core.Models;

public enum ActionRisk
{
    ReadOnly,
    Safe,
    Elevated,
    Destructive
}

public sealed record ActionProgress(string Message, double? Percent = null);

public sealed record ActionPreview(
    string Id,
    string Title,
    string Description,
    ActionRisk Risk,
    bool RequiresAdministrator,
    IReadOnlyList<string> AffectedResources);

public sealed record ActionExecutionContext(
    bool Confirmed,
    bool CreateRestorePoint,
    string Actor);

public sealed record ActionResult(
    bool Success,
    string Message,
    TimeSpan Duration,
    string? UndoToken = null,
    Exception? Error = null)
{
    public static ActionResult Failed(string message, Exception? error = null) =>
        new(false, message, TimeSpan.Zero, Error: error);
}

public sealed record ActionRequest(
    string Id,
    string Title,
    string Description,
    ActionRisk Risk,
    bool RequiresAdministrator,
    IReadOnlyList<string> AffectedResources,
    Func<CancellationToken, IProgress<ActionProgress>?, Task<ActionResult>> ExecuteAsync);

public sealed record AuditEntry(
    string ActionId,
    string Title,
    string Actor,
    bool Confirmed,
    bool Success,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? Error = null);
