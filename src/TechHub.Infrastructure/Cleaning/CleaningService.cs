using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Cleaning;

/// <summary>Limpeza restrita ao catálogo Windows, com plano imutável e exclusão por handle.</summary>
public sealed class CleaningService(IActionRunner actionRunner, IPowerShellExecutor powerShellExecutor) : ICleaningService
{
    private sealed record Root(string Path, CleaningScope Scope, bool Administrator, string Reason,
        string? Extension = null);
    private sealed record PlannedFile(Root Root, CleaningItem Item, uint Volume, ulong FileId);
    private sealed class Plan(PlannedFile[] files) { public PlannedFile[] Files { get; } = files; public int Used; }
    private readonly ConditionalWeakTable<CleaningPreview, Plan> plans = new();
    private readonly AsyncLocal<Guid?> executionPermit = new();
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".sys", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".com" };

    public async Task<CleaningPreview> AnalyzeAsync(CleaningOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Scopes);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A limpeza exige Windows.");
        if (!Enum.IsDefined(options.Mode) || options.Scopes.Any(scope => !Enum.IsDefined(scope)))
            throw new ArgumentException("Modo ou escopo de limpeza inválido.", nameof(options));
        var scopes = options.Scopes.Distinct().ToArray();
        if (options.Mode == CleaningMode.Safe && scopes.Any(scope => scope is
            CleaningScope.WindowsUpdateDownloads or CleaningScope.Prefetch or CleaningScope.RecycleBin or CleaningScope.LogsAndDumps))
            throw new ArgumentException("Este escopo exige o modo Profundo.", nameof(options));

        var issues = new List<CleaningIssue>();
        var roots = await GetRootsAsync(scopes, issues, cancellationToken).ConfigureAwait(false);
        var locations = options.Locations is null ? null : new HashSet<string>(options.Locations.Select(Path.GetFullPath), Paths);
        if (locations is not null)
            foreach (var location in locations)
                if (!roots.Any(root => location.StartsWith(Path.TrimEndingDirectorySeparator(root.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    issues.Add(new(location, "Localização fora dos roots permitidos; recusada."));
        return await Task.Run(() => Analyze(roots, options.Mode, issues, locations, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private CleaningPreview Analyze(List<Root> roots, CleaningMode mode, List<CleaningIssue> issues, HashSet<string>? locations, CancellationToken token)
    {
        var files = new List<PlannedFile>();
        var seen = new HashSet<string>(Paths);
        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(root.Path)) continue;
            var pending = new Stack<string>();
            pending.Push(root.Path);
            while (pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var locks = LockAncestors(directory, includeSelf: true);
                    foreach (var entry in Directory.GetFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (locations is not null && !locations.Contains(entry) && !locations.Any(location => location.StartsWith(entry + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                                throw new IOException("Links e reparse points são recusados.");
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                pending.Push(entry);
                                continue;
                            }
                            if (!IsAllowed(root, entry))
                                throw new IOException("Arquivo protegido ou fora do escopo permitido.");
                            using var handle = OpenFile(entry, delete: false);
                            var info = ReadInfo(handle);
                            if (info.NumberOfLinks != 1) throw new IOException("Arquivos com hard links são recusados.");
                            var written = ToDateTime(info.LastWriteTime);
                            if (mode == CleaningMode.Safe && written > cutoff) continue;
                            if (!seen.Add(entry)) continue;
                            files.Add(new PlannedFile(root, new CleaningItem(entry, Length(info), root.Reason,
                                root.Scope, written, ToDateTime(info.CreationTime)), info.VolumeSerialNumber, Identity(info)));
                        }
                        catch (Exception error) when (IsFileError(error)) { issues.Add(new(entry, error.Message)); }
                    }
                }
                catch (Exception error) when (IsFileError(error)) { issues.Add(new(directory, error.Message)); }
            }
        }
        var preview = new CleaningPreview(Guid.NewGuid(), mode,
            Array.AsReadOnly(files.Select(file => file.Item).ToArray()), Array.AsReadOnly(issues.ToArray()),
            files.Sum(file => file.Item.Bytes), files.Any(file => file.Root.Administrator));
        plans.Add(preview, new Plan(files.ToArray()));
        return preview;
    }

    public ActionRequest CreateActionRequest(CleaningPreview preview)
    {
        var plan = GetPlan(preview);
        return new ActionRequest($"cleaning.{preview.Id:N}", "Limpeza de arquivos",
            $"Modo {(preview.Mode == CleaningMode.Safe ? "Seguro" : "Profundo")}: excluir definitivamente {preview.Items.Count} arquivos ({preview.TotalBytes} bytes). Confirmação obrigatória; não há desfazer.",
            ActionRisk.Destructive, preview.RequiresAdministrator,
            Array.AsReadOnly(preview.Items.Select(item => $"{item.Location} | {item.Bytes} bytes | {item.Reason}").ToArray()),
            (token, progress) => executionPermit.Value == preview.Id
                ? DeleteAsync(plan, token, progress)
                : Task.FromResult(ActionResult.Failed("Execute a limpeza pelo serviço e pelo ActionRunner, com confirmação explícita.")));
    }

    public async Task<ActionResult> ExecuteAsync(CleaningPreview preview, ActionExecutionContext context,
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var request = CreateActionRequest(preview);
        var previous = executionPermit.Value;
        executionPermit.Value = preview.Id;
        try
        {
            // O token da operação vai ao executor; auditoria permanece possível após cancelamento parcial.
            var cancellable = request with { ExecuteAsync = (_, reporting) => request.ExecuteAsync(cancellationToken, reporting) };
            return await actionRunner.ExecuteAsync(cancellable, context, progress, CancellationToken.None).ConfigureAwait(false);
        }
        finally { executionPermit.Value = previous; }
    }

    private Plan GetPlan(CleaningPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!plans.TryGetValue(preview, out var plan))
            throw new ArgumentException("Prévia desconhecida ou alterada. Analise novamente.", nameof(preview));
        return plan;
    }

    private static Task<ActionResult> DeleteAsync(Plan plan, CancellationToken token, IProgress<ActionProgress>? progress) =>
        Task.Run(() =>
        {
            if (Interlocked.Exchange(ref plan.Used, 1) != 0)
                return ActionResult.Failed("Esta prévia já foi executada. Analise novamente.");
            var deleted = 0;
            long bytes = 0;
            var errors = new List<Exception>();
            foreach (var file in plan.Files)
            {
                if (token.IsCancellationRequested) { errors.Add(new OperationCanceledException("Limpeza cancelada.", token)); break; }
                try
                {
                    if (!IsAllowed(file.Root, file.Item.Location)) throw new IOException("Arquivo protegido ou fora do escopo permitido.");
                    using var locks = LockAncestors(file.Item.Location, includeSelf: false);
                    using var handle = OpenFile(file.Item.Location, delete: true);
                    var info = ReadInfo(handle);
                    if (info.NumberOfLinks != 1 || info.VolumeSerialNumber != file.Volume || Identity(info) != file.FileId ||
                        Length(info) != file.Item.Bytes || ToDateTime(info.LastWriteTime) != file.Item.LastWriteTimeUtc ||
                        ToDateTime(info.CreationTime) != file.Item.CreationTimeUtc)
                        throw new IOException("O arquivo mudou após a análise. Analise novamente.");
                    var disposition = new Disposition { DeleteFile = true };
                    if (!SetFileInformationByHandle(handle, 4, ref disposition, 1)) throw NativeError("Não foi possível excluir o arquivo");
                    deleted++;
                    bytes += file.Item.Bytes;
                }
                catch (Exception error) when (IsFileError(error))
                {
                    errors.Add(new IOException($"{file.Item.Location}: {error.Message}", error));
                }
                progress?.Report(new ActionProgress($"Excluídos {deleted} arquivos ({bytes} bytes); falhas: {errors.Count}.",
                    100.0 * (deleted + errors.Count) / Math.Max(1, plan.Files.Length)));
            }
            var message = $"Limpeza {(errors.Count == 0 ? "concluída" : "parcial ou cancelada")}: excluídos {deleted} de {plan.Files.Length} arquivos, {bytes} bytes; falhas: {errors.Count}.";
            return new ActionResult(errors.Count == 0, message, TimeSpan.Zero,
                Error: errors.Count == 0 ? null : new AggregateException("Falhas durante a limpeza.", errors));
        });

    private async Task<List<Root>> GetRootsAsync(CleaningScope[] scopes, List<CleaningIssue> issues, CancellationToken token)
    {
        var roots = new List<Root>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Add(CleaningScope scope, string parent, string relative, bool admin, string reason, string? extension = null)
        {
            if (scopes.Contains(scope) && !string.IsNullOrWhiteSpace(parent))
                roots.Add(new(Path.GetFullPath(Path.Combine(parent, relative)), scope, admin, reason, extension));
        }
        // Não aceitar TEMP/TMP arbitrários: usar os locais conhecidos, sem raízes fornecidas pelo chamador.
        Add(CleaningScope.UserTemp, local, "Temp", false, "Arquivo temporário do usuário");
        Add(CleaningScope.SystemTemp, windows, "Temp", true, "Arquivo temporário do sistema");
        Add(CleaningScope.WindowsUpdateDownloads, windows, @"SoftwareDistribution\Download", true, "Cache de downloads do Windows Update");
        Add(CleaningScope.Prefetch, windows, "Prefetch", true, "Cache Prefetch (será recriado)", ".pf");
        Add(CleaningScope.LogsAndDumps, local, "CrashDumps", false, "Dump de falha do usuário", ".dmp");
        Add(CleaningScope.LogsAndDumps, windows, "Minidump", true, "Minidump do Windows", ".dmp");
        Add(CleaningScope.LogsAndDumps, windows, @"Logs\CBS", true, "Log de manutenção CBS", ".log");
        Add(CleaningScope.LogsAndDumps, windows, @"Logs\DISM", true, "Log de manutenção DISM", ".log");
        Add(CleaningScope.LogsAndDumps, profile, @"AppData\Local\Microsoft\Windows\WER\ReportArchive", false, "Relatório de falha arquivado");
        foreach (var (scope, browser) in new[] { (CleaningScope.ChromeCache, @"Google\Chrome\User Data"), (CleaningScope.EdgeCache, @"Microsoft\Edge\User Data") })
        {
            if (!scopes.Contains(scope) || string.IsNullOrWhiteSpace(local)) continue;
            var userData = Path.Combine(local, browser);
            foreach (var directory in GetDirectories(userData, issues, token))
            {
                var name = Path.GetFileName(directory);
                if (name != "Default" && !(name.StartsWith("Profile ", StringComparison.Ordinal) && int.TryParse(name[8..], out _))) continue;
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                    roots.Add(new(Path.Combine(directory, cache), scope, false, "Cache do navegador; sessões, senhas e favoritos são preservados"));
            }
        }
        if (scopes.Contains(CleaningScope.FirefoxCache) && !string.IsNullOrWhiteSpace(local))
            foreach (var directory in GetDirectories(Path.Combine(local, @"Mozilla\Firefox\Profiles"), issues, token))
                roots.Add(new(Path.Combine(directory, "cache2"), CleaningScope.FirefoxCache, false, "Cache do Firefox"));
        if (scopes.Contains(CleaningScope.RecycleBin))
        {
            // Somente a lixeira do usuário atual em discos locais fixos. PowerShell descobre os volumes;
            // nunca usar Clear-RecycleBin, pois apagaria itens que chegaram depois do preview.
            token.ThrowIfCancellationRequested();
            try
            {
                var output = await powerShellExecutor.ExecuteAsync(
                    "$ErrorActionPreference = 'Stop'; ConvertTo-Json -Compress -InputObject @([System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' -and $_.IsReady } | ForEach-Object { $_.RootDirectory.FullName })", token).ConfigureAwait(false);
                var volumes = JsonSerializer.Deserialize<string[]>(output) ?? [];
                var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("SID do usuário indisponível.");
                foreach (var volume in volumes)
                {
                    var full = Path.GetFullPath(volume);
                    if (full.Length != 3 || full[1] != ':' || full[2] != '\\' || !char.IsAsciiLetter(full[0]))
                        throw new IOException("Volume de lixeira inválido.");
                    roots.Add(new(Path.Combine(full, "$Recycle.Bin", sid), CleaningScope.RecycleBin, false, "Arquivo da lixeira do usuário (exclusão definitiva)"));
                }
            }
            catch (Exception error) when (error is not OperationCanceledException) { issues.Add(new("Lixeira", $"Não foi possível analisar: {error.Message}")); }
        }
        return roots;
    }

    private static string[] GetDirectories(string path, List<CleaningIssue> issues, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(path)) return [];
        try
        {
            using var locks = LockAncestors(path, true);
            return Directory.GetDirectories(path).Where(directory => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        catch (Exception error) when (IsFileError(error)) { issues.Add(new(path, error.Message)); return []; }
    }

    private static bool IsAllowed(Root root, string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || full.IndexOf(':', 2) >= 0 || full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var attributes = File.GetAttributes(full);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.ReadOnly | FileAttributes.System)) != 0) return false;
        var extension = Path.GetExtension(full);
        return !ProtectedExtensions.Contains(extension) && (root.Extension is null || Paths.Equals(root.Extension, extension));
    }

    private sealed class HandleList : IDisposable
    {
        public List<SafeFileHandle> Handles { get; } = [];
        public void Dispose() { foreach (var handle in Handles.AsEnumerable().Reverse()) handle.Dispose(); }
    }

    private static HandleList LockAncestors(string path, bool includeSelf)
    {
        var handles = new HandleList();
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.IndexOf(':', 2) >= 0) throw new IOException("Caminho remoto ou alternativo recusado.");
            var directory = includeSelf ? full : Path.GetDirectoryName(full)!;
            var ancestors = new Stack<string>();
            while (directory is not null) { ancestors.Push(directory); directory = Path.GetDirectoryName(directory); }
            foreach (var ancestor in ancestors)
            {
                // Sem compartilhamento de escrita/exclusão: impede trocar ancestrais ou instalar reparse points.
                var handle = CreateFile(ancestor, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw NativeError("Não foi possível proteger o diretório"); }
                handles.Handles.Add(handle);
                var info = ReadInfo(handle);
                if ((info.FileAttributes & 0x400) != 0 || (info.FileAttributes & 0x10) == 0)
                    throw new IOException("Diretório inválido ou reparse point recusado.");
            }
            return handles;
        }
        catch { handles.Dispose(); throw; }
    }

    private static SafeFileHandle OpenFile(string path, bool delete)
    {
        var handle = CreateFile(path, delete ? 0x10080u : 0x80u, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw NativeError("Não foi possível abrir o arquivo"); }
        try
        {
            var info = ReadInfo(handle);
            if ((info.FileAttributes & (0x400 | 0x10 | 0x1 | 0x4)) != 0) throw new IOException("Arquivo protegido ou reparse point recusado.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException;
    private static IOException NativeError(string message) => new($"{message}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    private static long Length(FileInfoNative info) => checked((long)(((ulong)info.FileSizeHigh << 32) | info.FileSizeLow));
    private static ulong Identity(FileInfoNative info) => ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
    private static DateTime ToDateTime(System.Runtime.InteropServices.ComTypes.FILETIME time) => DateTime.FromFileTimeUtc(((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime);
    private static FileInfoNative ReadInfo(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw NativeError("Não foi possível revalidar o arquivo");
        return info;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoNative
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref Disposition information, uint size);
}
