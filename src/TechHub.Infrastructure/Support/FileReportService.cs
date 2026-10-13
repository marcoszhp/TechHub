using System.Text;
using System.Text.Json;
using System.IO;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Support;

public sealed class FileReportService(ITicketService ticketService) : IReportService
{
    public async Task<string> ExportTicketsCsvAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var path = ValidateDestination(destinationPath, ".csv");
        var tickets = await ticketService.GetTicketsAsync(cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder("Id;Título;Categoria;Prioridade;Status;Solicitante;Criado;Atualizado;Anexo\r\n");
        foreach (var ticket in tickets)
            builder.AppendJoin(';', ticket.Id, Quote(ticket.Title), Quote(ticket.Category.ToString()), Quote(ticket.Priority.ToString()), Quote(ticket.Status.ToString()), Quote(ticket.Requester), ticket.CreatedAt.ToString("O"), ticket.UpdatedAt.ToString("O"), Quote(ticket.DiagnosticAttachmentPath ?? "")).Append("\r\n");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<string> ExportTicketsJsonAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var path = ValidateDestination(destinationPath, ".json");
        var tickets = await ticketService.GetTicketsAsync(cancellationToken).ConfigureAwait(false);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, tickets, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string ValidateDestination(string value, string extension)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) || !value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Informe um caminho absoluto com extensão {extension}.", nameof(value));
        var full = Path.GetFullPath(value);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
