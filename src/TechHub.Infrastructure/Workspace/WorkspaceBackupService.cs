using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Workspace;

public sealed class WorkspaceBackupService(IApplicationPaths paths) : IWorkspaceBackupService
{
    public const long MaxExpandedBytes = 256L * 1024 * 1024;
    public const int MaxEntries = 512;
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Manifest(string Format, int Version, DateTimeOffset CreatedAt, int SettingsFiles);

    public Task<WorkspaceBackupResult> ExportAsync(string zipPath, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var scratch = Path.Combine(Path.GetTempPath(), "TechHub-workspace-" + Guid.NewGuid().ToString("N"));
            string? pendingZip = null;
            try
            {
                var target = Path.GetFullPath(zipPath);
                if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Escolha um arquivo .zip.");
                RejectActiveOverlap(target);
                EnsureNoReparseAncestors(target);
                if (!File.Exists(paths.Current.DatabasePath)) throw new FileNotFoundException("Banco TechHub ainda não existe.");
                Directory.CreateDirectory(scratch);
                var snapshot = Path.Combine(scratch, "techhub.db");
                using (var source = Connection(paths.Current.DatabasePath, SqliteOpenMode.ReadOnly))
                using (var destination = Connection(snapshot, SqliteOpenMode.ReadWriteCreate))
                {
                    source.Open(); destination.Open();
                    // Official online backup API includes committed WAL pages; never copy the live database file.
                    source.BackupDatabase(destination);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var settings = SettingsFiles().ToArray();
                if (settings.Length + 2 > MaxEntries) throw new InvalidDataException("Há mais de 510 arquivos de configurações.");
                var total = new FileInfo(snapshot).Length + settings.Sum(f => new FileInfo(f).Length);
                if (total > MaxExpandedBytes - 4096) throw new InvalidDataException("Backup excede o limite de 256 MB.");
                pendingZip = Path.Combine(Path.GetDirectoryName(target)!, ".techhub-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(pendingZip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    long remaining = MaxExpandedBytes;
                    remaining -= await AddFileAsync(archive, snapshot, "techhub.db", remaining, cancellationToken).ConfigureAwait(false);
                    foreach (var file in settings)
                    {
                        var relative = Path.GetRelativePath(paths.Current.DataDirectory, file).Replace('\\', '/');
                        ValidateEntryName("settings/" + relative);
                        remaining -= await AddFileAsync(archive, file, "settings/" + relative, remaining, cancellationToken).ConfigureAwait(false);
                    }
                    await using var manifest = archive.CreateEntry("manifest.json").Open();
                    await JsonSerializer.SerializeAsync(manifest, new Manifest("TechHub.Workspace", 1, DateTimeOffset.UtcNow, settings.Length), cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                // Publish only a complete archive. SaveFileDialog is responsible for overwrite confirmation.
                File.Move(pendingZip, target, overwrite: true);
                pendingZip = null;
                return new WorkspaceBackupResult(target, settings.Length, new FileInfo(target).Length);
            }
            finally
            {
                if (pendingZip is not null && File.Exists(pendingZip)) File.Delete(pendingZip);
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
                gate.Release();
            }
        }, cancellationToken);

    public Task<WorkspaceBackupResult> RestoreAsync(string zipPath, string emptyFolder, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string? staging = null;
            try
            {
                var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(emptyFolder));
                RejectActiveOverlap(target);
                EnsureEmptyFolder(target);
                if (new FileInfo(zipPath).Length > MaxExpandedBytes) throw new InvalidDataException("ZIP excede 256 MB.");
                using var archive = ZipFile.OpenRead(zipPath);
                if (archive.Entries.Count is < 2 or > MaxEntries) throw new InvalidDataException("Quantidade de arquivos inválida.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    ValidateEntryName(entry.FullName);
                    var type = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (type != 0 && type != 0x8000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Links e entradas especiais não são permitidos.");
                    if (!names.Add(entry.FullName)) throw new InvalidDataException("Arquivo duplicado no ZIP.");
                    if (entry.Length < 0 || entry.Length > MaxExpandedBytes - total) throw new InvalidDataException("Conteúdo expandido excede 256 MB.");
                    total += entry.Length;
                }
                if (!names.Contains("techhub.db") || !names.Contains("manifest.json")) throw new InvalidDataException("Este ZIP não é um backup Workspace.");
                // Validate metadata before extraction. Bound its read independently of claimed ZIP lengths.
                var manifestEntry = archive.Entries.Single(e => e.FullName == "manifest.json");
                if (manifestEntry.Length > 4096) throw new InvalidDataException("Manifesto inválido.");
                using (var bytes = new MemoryStream())
                {
                    await using var input = manifestEntry.Open();
                    await CopyBoundedAsync(input, bytes, 4096, cancellationToken).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<Manifest>(bytes.ToArray());
                    if (manifest is null || manifest.Format != "TechHub.Workspace" || manifest.Version != 1
                        || manifest.SettingsFiles != names.Count(n => n.StartsWith("settings/", StringComparison.Ordinal)))
                        throw new InvalidDataException("Formato ou versão de backup não suportado.");
                }
                staging = Path.Combine(Path.GetDirectoryName(target)!, ".techhub-restore-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                long remaining = MaxExpandedBytes;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Settings retain their Data-relative paths; manifest remains beside the restored database.
                    var name = entry.FullName.StartsWith("settings/", StringComparison.Ordinal) ? entry.FullName[9..] : entry.FullName;
                    if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) && entry.FullName != "manifest.json")
                        throw new InvalidDataException("Conflito de nomes de configurações.");
                    var file = Path.GetFullPath(Path.Combine(staging, name));
                    if (!IsWithin(file, staging)) throw new InvalidDataException("Caminho fora da pasta de restauração.");
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await using var input = entry.Open();
                    await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var copied = await CopyBoundedAsync(input, output, remaining, cancellationToken).ConfigureAwait(false);
                    if (copied != entry.Length) throw new InvalidDataException("Tamanho de entrada inconsistente.");
                    remaining -= copied;
                }
                // Verify a real SQLite snapshot, not just a file bearing the expected name.
                using (var db = Connection(Path.Combine(staging, "techhub.db"), SqliteOpenMode.ReadOnly))
                {
                    db.Open();
                    using var check = db.CreateCommand();
                    check.CommandText = "PRAGMA quick_check";
                    if (!string.Equals(Convert.ToString(check.ExecuteScalar()), "ok", StringComparison.Ordinal))
                        throw new InvalidDataException("O banco do backup falhou na verificação SQLite.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                EnsureEmptyFolder(target); // Recheck immediately before publishing; no live data is opened for writing.
                Directory.Delete(target, recursive: false);
                try { Directory.Move(staging, target); staging = null; }
                catch { if (!Directory.Exists(target)) Directory.CreateDirectory(target); throw; }
                return new WorkspaceBackupResult(target, names.Count - 2, total);
            }
            finally
            {
                if (staging is not null && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                gate.Release();
            }
        }, cancellationToken);

    private IEnumerable<string> SettingsFiles()
    {
        // Data JSON files include settings and automations. Logs/attachments/reports are intentionally excluded.
        var pending = new Stack<string>();
        pending.Push(paths.Current.DataDirectory);
        while (pending.TryPop(out var directory))
        {
            EnsureNoReparseAncestors(directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                EnsureNoReparseAncestors(file);
                if (!Path.GetFileName(file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (new[] { "Logs", "Attachments", "Reports" }.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Configurações contêm um link de diretório.");
                pending.Push(child);
            }
        }
    }

    private static async Task<long> AddFileAsync(ZipArchive archive, string file, string name, long limit, CancellationToken ct)
    {
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        return await CopyBoundedAsync(input, output, limit, ct).ConfigureAwait(false);
    }

    private static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (count > limit - total) throw new InvalidDataException("Conteúdo excede o limite permitido.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            total += count;
        }
        return total;
    }

    private static SqliteConnection Connection(string file, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder
        { DataSource = file, Mode = mode, Pooling = false, DefaultTimeout = 5 }.ToString());

    private void RejectActiveOverlap(string path)
    {
        var data = Path.GetFullPath(paths.Current.DataDirectory);
        if (IsWithin(path, data) || IsWithin(data, path) || path.Equals(Path.GetFullPath(paths.Current.DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha um destino fora da pasta de dados em uso.");
    }

    private static bool IsWithin(string path, string root) => path.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureEmptyFolder(string path)
    {
        if (Path.GetPathRoot(path)!.Equals(path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Não use a raiz de um disco.");
        EnsureNoReparseAncestors(path);
        if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
            throw new InvalidOperationException("Crie e selecione uma pasta nova e vazia para restaurar.");
    }

    private static void EnsureNoReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links simbólicos e pontos de redirecionamento não são permitidos no caminho.");
        }
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Contains('\\') || Path.IsPathRooted(name) || name.Contains(':'))
            throw new InvalidDataException("Caminho inseguro no ZIP.");
        foreach (var part in name.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Nome inseguro no ZIP.");
        }
        if (name != "techhub.db" && name != "manifest.json" && !(name.StartsWith("settings/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("O ZIP contém arquivos fora do formato TechHub Workspace.");
    }
}
