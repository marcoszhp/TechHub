namespace TechHub.Core.Models;

public enum TicketCategory
{
    Hardware,
    Software,
    Rede,
    Impressora,
    Segurança,
    Outro
}

public enum TicketPriority
{
    Baixa,
    Normal,
    Alta,
    Crítica
}

public enum TicketStatus
{
    Aberto,
    EmAtendimento,
    AguardandoUsuário,
    Resolvido,
    Fechado
}

public sealed record SupportTicket(
    long Id,
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    string Requester,
    string? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DiagnosticAttachmentPath = null);

public sealed record TicketComment(
    long Id,
    long TicketId,
    string Author,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record CreateTicketRequest(
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    string Requester);

public sealed record SoftwareInfo(
    string Name,
    string Version,
    string Publisher,
    string? InstallLocation,
    string Source);

public sealed record SoftwareUpgradeSummary(
    int Count,
    string Message,
    IReadOnlyList<string> Packages);
