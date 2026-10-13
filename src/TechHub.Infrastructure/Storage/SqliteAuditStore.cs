using Microsoft.Data.Sqlite;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Storage;

public sealed class SqliteAuditStore : IAuditStore
{
    private readonly IApplicationPaths _paths;
    private readonly Task _initialization;

    public SqliteAuditStore(IApplicationPaths paths)
    {
        _paths = paths;
        _initialization = InitializeAsync();
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _initialization.ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_log
                (action_id, title, actor, confirmed, success, message, started_at, completed_at, error)
            VALUES
                ($actionId, $title, $actor, $confirmed, $success, $message, $startedAt, $completedAt, $error);
            """;
        command.Parameters.AddWithValue("$actionId", entry.ActionId);
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$actor", entry.Actor);
        command.Parameters.AddWithValue("$confirmed", entry.Confirmed ? 1 : 0);
        command.Parameters.AddWithValue("$success", entry.Success ? 1 : 0);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$startedAt", entry.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", entry.CompletedAt.ToString("O"));
        command.Parameters.AddWithValue("$error", (object?)entry.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await _initialization.ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT action_id, title, actor, confirmed, success, message, started_at, completed_at, error
            FROM audit_log
            ORDER BY completed_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        var result = new List<AuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AuditEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4) == 1,
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6)),
                DateTimeOffset.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    private SqliteConnection CreateConnection() => new($"Data Source={_paths.Current.DatabasePath}");

    private async Task InitializeAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                action_id TEXT NOT NULL,
                title TEXT NOT NULL,
                actor TEXT NOT NULL,
                confirmed INTEGER NOT NULL,
                success INTEGER NOT NULL,
                message TEXT NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                error TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_audit_log_completed_at ON audit_log(completed_at DESC);
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
