using System.IO;
using Microsoft.Data.Sqlite;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Workspace;

public sealed class SqliteWorkspaceClientStore(IApplicationPaths paths) : IWorkspaceClientStore
{
    private readonly SemaphoreSlim initialization = new(1, 1);
    private bool initialized;

    public Task<IReadOnlyList<WorkspaceClient>> FindClientsAsync(string search = "", CancellationToken cancellationToken = default) =>
        Background<IReadOnlyList<WorkspaceClient>>(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,Name,Contact,Notes FROM workspace_clients WHERE instr(lower(Name || ' ' || Contact || ' ' || Notes),lower($search))>0 ORDER BY Name,Id";
            command.Parameters.AddWithValue("$search", search.Trim());
            var result = new List<WorkspaceClient>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return result;
        }, cancellationToken);

    public Task<WorkspaceClient> SaveClientAsync(WorkspaceClient client, CancellationToken cancellationToken = default) =>
        Background(async () =>
        {
            Validate(client.Id, client.Name, client.Contact, client.Notes);
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = client.Id == 0
                ? "INSERT INTO workspace_clients(Name,Contact,Notes) VALUES($name,$contact,$notes); SELECT last_insert_rowid();"
                : "UPDATE workspace_clients SET Name=$name,Contact=$contact,Notes=$notes WHERE Id=$id RETURNING Id;";
            command.Parameters.AddWithValue("$id", client.Id);
            command.Parameters.AddWithValue("$name", client.Name.Trim());
            command.Parameters.AddWithValue("$contact", client.Contact.Trim());
            command.Parameters.AddWithValue("$notes", client.Notes.Trim());
            var id = await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Cliente não encontrado.");
            return client with { Id = Convert.ToInt64(id), Name = client.Name.Trim(), Contact = client.Contact.Trim(), Notes = client.Notes.Trim() };
        }, cancellationToken);

    public Task<IReadOnlyList<WorkspaceEquipment>> FindEquipmentAsync(long clientId, string search = "", CancellationToken cancellationToken = default) =>
        Background<IReadOnlyList<WorkspaceEquipment>>(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,ClientId,Name,SerialNumber,Notes FROM workspace_equipment WHERE ClientId=$client AND instr(lower(Name || ' ' || SerialNumber || ' ' || Notes),lower($search))>0 ORDER BY Name,Id";
            command.Parameters.AddWithValue("$client", clientId);
            command.Parameters.AddWithValue("$search", search.Trim());
            var result = new List<WorkspaceEquipment>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            return result;
        }, cancellationToken);

    public Task<WorkspaceEquipment> SaveEquipmentAsync(WorkspaceEquipment equipment, CancellationToken cancellationToken = default) =>
        Background(async () =>
        {
            Validate(equipment.Id, equipment.Name, equipment.SerialNumber, equipment.Notes);
            if (equipment.ClientId <= 0) throw new ArgumentException("Selecione um cliente salvo.");
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = equipment.Id == 0
                ? "INSERT INTO workspace_equipment(ClientId,Name,SerialNumber,Notes) VALUES($client,$name,$serial,$notes); SELECT last_insert_rowid();"
                : "UPDATE workspace_equipment SET Name=$name,SerialNumber=$serial,Notes=$notes WHERE Id=$id AND ClientId=$client RETURNING Id;";
            command.Parameters.AddWithValue("$id", equipment.Id);
            command.Parameters.AddWithValue("$client", equipment.ClientId);
            command.Parameters.AddWithValue("$name", equipment.Name.Trim());
            command.Parameters.AddWithValue("$serial", equipment.SerialNumber.Trim());
            command.Parameters.AddWithValue("$notes", equipment.Notes.Trim());
            var id = await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Equipamento não encontrado para este cliente.");
            return equipment with { Id = Convert.ToInt64(id), Name = equipment.Name.Trim(), SerialNumber = equipment.SerialNumber.Trim(), Notes = equipment.Notes.Trim() };
        }, cancellationToken);

    private static void Validate(long id, string name, string detail, string notes)
    {
        if (id < 0 || string.IsNullOrWhiteSpace(name) || name.Length > 200 || detail.Length > 500 || notes.Length > 4000)
            throw new ArgumentException("Informe um nome (até 200 caracteres), contato/série (até 500) e notas (até 4000).");
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        await initialization.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.Current.DataDirectory);
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = paths.Current.DatabasePath, ForeignKeys = true, DefaultTimeout = 5 }.ToString());
            try
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                if (!initialized)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        CREATE TABLE IF NOT EXISTS workspace_clients(Id INTEGER PRIMARY KEY,Name TEXT NOT NULL,Contact TEXT NOT NULL,Notes TEXT NOT NULL);
                        CREATE TABLE IF NOT EXISTS workspace_equipment(Id INTEGER PRIMARY KEY,ClientId INTEGER NOT NULL REFERENCES workspace_clients(Id),Name TEXT NOT NULL,SerialNumber TEXT NOT NULL,Notes TEXT NOT NULL);
                        CREATE INDEX IF NOT EXISTS IX_workspace_equipment_client ON workspace_equipment(ClientId);
                        """;
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    initialized = true;
                }
                return connection;
            }
            catch { await connection.DisposeAsync(); throw; }
        }
        finally { initialization.Release(); }
    }

    // Microsoft.Data.Sqlite async methods can execute synchronously; keep disk/lock waits off the WPF dispatcher.
    private static Task<T> Background<T>(Func<Task<T>> operation, CancellationToken ct) => Task.Run(operation, ct);
}
