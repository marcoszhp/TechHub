using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface ITicketService
{
    Task<IReadOnlyList<SupportTicket>> GetTicketsAsync(CancellationToken cancellationToken = default);
    Task<SupportTicket> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TicketComment>> GetCommentsAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<TicketComment> AddCommentAsync(long ticketId, string author, string body, CancellationToken cancellationToken = default);
    Task<SupportTicket> SetStatusAsync(long ticketId, TicketStatus status, CancellationToken cancellationToken = default);
    Task<string> CollectDiagnosticsAsync(long ticketId, CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<string> ExportTicketsCsvAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<string> ExportTicketsJsonAsync(string destinationPath, CancellationToken cancellationToken = default);
}

public interface ISoftwareService
{
    Task<IReadOnlyList<SoftwareInfo>> GetInventoryAsync(CancellationToken cancellationToken = default);
    Task<SoftwareUpgradeSummary> GetUpgradeSummaryAsync(CancellationToken cancellationToken = default);
    ActionRequest CreateUpgradeAllRequest();
}
