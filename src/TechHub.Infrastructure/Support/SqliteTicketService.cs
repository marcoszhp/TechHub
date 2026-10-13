using System.IO.Compression;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Support;

public sealed class SqliteTicketService(
    IApplicationPaths applicationPaths,
    IDashboardService dashboardService) : ITicketService
{
    private readonly SemaphoreSlim initialization = new(1, 1);
    private bool initialized;

    public async Task<IReadOnlyList<SupportTicket>> GetTicketsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Description, Category, Priority, Status, Requester, Assignee, CreatedAt, UpdatedAt, DiagnosticAttachmentPath FROM Tickets ORDER BY UpdatedAt DESC";
        var result = new List<SupportTicket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTicket(reader));
        return result;
    }

    public async Task<SupportTicket> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Requester);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Tickets(Title, Description, Category, Priority, Status, Requester, CreatedAt, UpdatedAt) VALUES($title,$description,$category,$priority,$status,$requester,$created,$updated); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$title", request.Title.Trim());
        command.Parameters.AddWithValue("$description", request.Description.Trim());
        command.Parameters.AddWithValue("$category", request.Category.ToString());
        command.Parameters.AddWithValue("$priority", request.Priority.ToString());
        command.Parameters.AddWithValue("$status", TicketStatus.Aberto.ToString());
        command.Parameters.AddWithValue("$requester", request.Requester.Trim());
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return new SupportTicket(id, request.Title.Trim(), request.Description.Trim(), request.Category, request.Priority, TicketStatus.Aberto, request.Requester.Trim(), null, now, now);
    }

    public async Task<IReadOnlyList<TicketComment>> GetCommentsAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, TicketId, Author, Body, CreatedAt FROM TicketComments WHERE TicketId=$ticket ORDER BY CreatedAt";
        command.Parameters.AddWithValue("$ticket", ticketId);
        var result = new List<TicketComment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4))));
        return result;
    }

    public async Task<TicketComment> AddCommentAsync(long ticketId, string author, string body, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO TicketComments(TicketId, Author, Body, CreatedAt) VALUES($ticket,$author,$body,$created); UPDATE Tickets SET UpdatedAt=$created WHERE Id=$ticket; SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$ticket", ticketId);
        command.Parameters.AddWithValue("$author", author.Trim());
        command.Parameters.AddWithValue("$body", body.Trim());
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return new TicketComment(id, ticketId, author.Trim(), body.Trim(), now);
    }

    public async Task<SupportTicket> SetStatusAsync(long ticketId, TicketStatus status, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Tickets SET Status=$status, UpdatedAt=$updated WHERE Id=$id";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$id", ticketId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("Chamado não encontrado.");
        var ticket = (await GetTicketsAsync(cancellationToken).ConfigureAwait(false)).Single(item => item.Id == ticketId);
        return ticket;
    }

    public async Task<string> CollectDiagnosticsAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var ticket = (await GetTicketsAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.Id == ticketId)
            ?? throw new InvalidOperationException("Chamado não encontrado.");
        var directory = Path.Combine(applicationPaths.Current.DataDirectory, "Attachments");
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, $"ticket-{ticketId}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
        await using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var snapshot = await dashboardService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var entry = archive.CreateEntry("dashboard.json");
            await using var entryStream = entry.Open();
            await JsonSerializer.SerializeAsync(entryStream, snapshot, cancellationToken: cancellationToken).ConfigureAwait(false);
            var ticketEntry = archive.CreateEntry("ticket.json");
            await using var ticketStream = ticketEntry.Open();
            await JsonSerializer.SerializeAsync(ticketStream, ticket, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Tickets SET DiagnosticAttachmentPath=$path, UpdatedAt=$updated WHERE Id=$id";
        command.Parameters.AddWithValue("$path", zipPath);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", ticketId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return zipPath;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized) return;
        await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized) return;
            Directory.CreateDirectory(applicationPaths.Current.DataDirectory);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS Tickets(Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, Description TEXT NOT NULL, Category TEXT NOT NULL, Priority TEXT NOT NULL, Status TEXT NOT NULL, Requester TEXT NOT NULL, Assignee TEXT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DiagnosticAttachmentPath TEXT NULL); CREATE TABLE IF NOT EXISTS TicketComments(Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketId INTEGER NOT NULL REFERENCES Tickets(Id) ON DELETE CASCADE, Author TEXT NOT NULL, Body TEXT NOT NULL, CreatedAt TEXT NOT NULL); CREATE INDEX IF NOT EXISTS IX_Tickets_UpdatedAt ON Tickets(UpdatedAt);";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            initialized = true;
        }
        finally { initialization.Release(); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={applicationPaths.Current.DatabasePath}");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static SupportTicket ReadTicket(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
        Enum.Parse<TicketCategory>(reader.GetString(3)), Enum.Parse<TicketPriority>(reader.GetString(4)),
        Enum.Parse<TicketStatus>(reader.GetString(5)), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
        DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9)), reader.IsDBNull(10) ? null : reader.GetString(10));
}
