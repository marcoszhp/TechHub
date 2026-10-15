using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Updates;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string Repository = "https://api.github.com/repos/marcoszhp/TechHub/releases/latest";
    private const string Releases = "https://github.com/marcoszhp/TechHub/releases";
    public const long MaximumSizeBytes = 512L * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly string _current;
    private readonly Func<UpdateInstallMode> _mode;
    private readonly string _staging;
    private readonly Architecture _architecture;
    private readonly TimeSpan _downloadTimeout;

    public GitHubUpdateService() : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0",
        () => DetectInstallMode(AppContext.BaseDirectory),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechHub", "Updates"),
        RuntimeInformation.ProcessArchitecture) { }

    // Injected clients belong to the caller; use infinite Timeout for the separate deadlines.
    public GitHubUpdateService(HttpClient client, string currentVersion, Func<UpdateInstallMode> installMode,
        string stagingRoot, Architecture architecture = Architecture.X64, TimeSpan? downloadTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _current = currentVersion;
        _mode = installMode ?? throw new ArgumentNullException(nameof(installMode));
        _staging = Path.GetFullPath(stagingRoot);
        _architecture = architecture;
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(10);
        if (_downloadTimeout <= TimeSpan.Zero || _downloadTimeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(downloadTimeout));
    }

    public static UpdateInstallMode DetectInstallMode(string directory, string? localAppData = null, string? programFiles = null)
    {
        try
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            RejectReparsePoints(directory);
            if (File.Exists(Path.Combine(directory, "portable.flag"))) return UpdateInstallMode.Portable;
            if (!File.Exists(Path.Combine(directory, "TechHub.App.exe")) || !File.Exists(Path.Combine(directory, "unins000.exe")))
                return UpdateInstallMode.Unknown;
            var user = Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechHub");
            var machine = Path.Combine(programFiles ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TechHub");
            var mode = string.Equals(directory, Path.GetFullPath(user), StringComparison.OrdinalIgnoreCase) ? UpdateInstallMode.User
                : string.Equals(directory, Path.GetFullPath(machine), StringComparison.OrdinalIgnoreCase) ? UpdateInstallMode.Machine : UpdateInstallMode.Unknown;
            var marker = Path.Combine(directory, "install-mode.txt");
            if (File.Exists(marker))
            {
                RejectReparsePoints(marker);
                if (new FileInfo(marker).Length > 32) return UpdateInstallMode.Unknown;
                var expected = mode == UpdateInstallMode.User ? "user" : mode == UpdateInstallMode.Machine ? "machine" : "";
                if (!string.Equals(File.ReadAllText(marker).Trim(), expected, StringComparison.OrdinalIgnoreCase)) return UpdateInstallMode.Unknown;
            }
            return mode;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return UpdateInstallMode.Unknown; }
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        var mode = UpdateInstallMode.Unknown;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            mode = _mode();
            if (_architecture != Architecture.X64 || mode is not (UpdateInstallMode.User or UpdateInstallMode.Machine))
                return Result(UpdateCheckStatus.Unsupported, mode, "Esta instalação exige atualização manual (somente instalações padrão x64 são compatíveis).");
            ParseVersion(_current);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var release = await ReadReleaseAsync(mode, deadline.Token).ConfigureAwait(false);
            var available = ParseVersion(release.LatestVersion) > ParseVersion(_current);
            return release with { IsAvailable = available, Status = available ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate,
                Message = available ? $"Nova versão disponível: {release.LatestVersion}." : $"Você está usando a versão {_current}, a mais recente." };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or JsonException or IOException or InvalidDataException or OperationCanceledException
            or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { return Result(UpdateCheckStatus.Failed, mode, "Não foi possível verificar atualizações. Tente novamente mais tarde."); }
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateInfo update, IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(update);
        // Re-fetch trusted metadata before disk IO, including digest and size.
        using (var queryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            queryDeadline.CancelAfter(TimeSpan.FromSeconds(30));
            var trusted = await ReadReleaseAsync(update.InstallMode, queryDeadline.Token).ConfigureAwait(false);
            if (trusted.LatestVersion != update.LatestVersion || trusted.DownloadUrl != update.DownloadUrl
                || trusted.ReleaseUrl != update.ReleaseUrl || trusted.SizeBytes != update.SizeBytes
                || !string.Equals(trusted.Sha256, update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Os metadados da atualização foram alterados. Verifique novamente.");
        }
        Validate(update);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_downloadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("TechHub/1.3.0");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateResponseUri(response.RequestMessage?.RequestUri ?? request.RequestUri!);
        if (response.Content.Headers.ContentLength is long length && length != update.SizeBytes)
            throw new InvalidDataException("O tamanho informado pelo servidor não corresponde ao pacote.");
        deadline.Token.ThrowIfCancellationRequested();
        using var parentGuard = StagingDirectoryGuard.Acquire(_staging);
        RejectReparsePoints(_staging);
        Directory.CreateDirectory(_staging);
        using var rootGuard = StagingDirectoryGuard.Acquire(_staging);
        RejectReparsePoints(_staging);
        var directory = Path.Combine(_staging, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("A pasta de atualização já existe.");
        Directory.CreateDirectory(directory);
        using var directoryGuard = StagingDirectoryGuard.Acquire(directory);
        RejectReparsePoints(directory);
        var final = Path.Combine(directory, AssetName(update.InstallMode, update.LatestVersion));
        var partial = final + ".partial";
        var ownsPartial = false;
        try
        {
            await using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                ownsPartial = true;
                await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long received = 0;
                progress?.Report(new UpdateProgress(0, update.SizeBytes));
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false)) != 0)
                {
                    received += count;
                    if (received > update.SizeBytes || received > MaximumSizeBytes) throw new InvalidDataException("O download excedeu o tamanho permitido.");
                    hash.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                    progress?.Report(new UpdateProgress(received, update.SizeBytes));
                }
                if (received != update.SizeBytes || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Sha256!)))
                    throw new InvalidDataException("O pacote está incompleto ou falhou na verificação SHA256.");
                await destination.FlushAsync(deadline.Token).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
            }
            deadline.Token.ThrowIfCancellationRequested();
            Validate(update);
            RejectReparsePoints(partial);
            File.Move(partial, final, overwrite: false);
            ownsPartial = false;
            return new DownloadedUpdate(update, final);
        }
        finally
        {
            if (ownsPartial)
            {
                // Delete only our partial; never follow a replaced parent or recursively delete staging.
                try { RejectReparsePoints(partial); File.Delete(partial); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private async Task<UpdateInfo> ReadReleaseAsync(UpdateInstallMode mode, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Repository);
        request.Headers.UserAgent.ParseAdd("TechHub/1.3.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is Uri uri && uri.AbsoluteUri != Repository)
            throw new InvalidDataException("A consulta foi redirecionada para um endereço não permitido.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (data.Length + count > 1024 * 1024) throw new InvalidDataException("Os metadados excederam o limite permitido.");
            data.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(data.ToArray());
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("A versão publicada não é estável.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v')) throw new InvalidDataException("A tag da versão não está no formato permitido.");
        var version = tag[1..];
        ParseVersion(version);
        var releaseUrl = root.GetProperty("html_url").GetString();
        if (releaseUrl != $"{Releases}/tag/{tag}") throw new InvalidDataException("Endereço da versão inválido.");
        var name = AssetName(mode, version);
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("O instalador desta modalidade não está disponível.");
        var asset = assets[0];
        var size = asset.GetProperty("size").GetInt64();
        var digest = asset.GetProperty("digest").GetString() ?? "";
        var url = asset.GetProperty("browser_download_url").GetString();
        if (size < 1 || size > MaximumSizeBytes || !Regex.IsMatch(digest, "\\Asha256:[0-9a-fA-F]{64}\\z")
            || url != $"{Releases}/download/{tag}/{name}") throw new InvalidDataException("Metadados do instalador inválidos.");
        return new UpdateInfo(true, _current, version, releaseUrl!, url, "Atualização pronta para download.",
            UpdateCheckStatus.Available, mode, digest[7..], size,
            root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null);
    }

    private void Validate(UpdateInfo update)
    {
        if (_architecture != Architecture.X64 || update.InstallMode is not (UpdateInstallMode.User or UpdateInstallMode.Machine)
            || update.InstallMode != _mode() || !update.IsAvailable || update.Status != UpdateCheckStatus.Available
            || update.CurrentVersion != _current || ParseVersion(update.LatestVersion) <= ParseVersion(_current)
            || update.SizeBytes < 1 || update.SizeBytes > MaximumSizeBytes || update.Sha256 is null
            || !Regex.IsMatch(update.Sha256, "\\A[0-9a-fA-F]{64}\\z"))
            throw new InvalidDataException("A atualização não é válida para esta instalação.");
        var name = AssetName(update.InstallMode, update.LatestVersion);
        if (update.ReleaseUrl != $"{Releases}/tag/v{update.LatestVersion}")
            throw new InvalidDataException("Endereço da versão inválido.");
        var tag = update.ReleaseUrl[(update.ReleaseUrl.LastIndexOf('/') + 1)..];
        if (update.DownloadUrl != $"{Releases}/download/{tag}/{name}") throw new InvalidDataException("Endereço do pacote inválido.");
    }

    private static Version ParseVersion(string version)
    {
        if (version is null || !Regex.IsMatch(version, "\\A(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})\\z") || !Version.TryParse(version, out var parsed))
            throw new InvalidDataException("Número de versão inválido.");
        return parsed;
    }

    private static string AssetName(UpdateInstallMode mode, string version) => mode == UpdateInstallMode.User
        ? $"TechHub-User-Setup-{version}.exe" : $"TechHub-Setup-{version}.exe";

    private UpdateInfo Result(UpdateCheckStatus status, UpdateInstallMode mode, string message) =>
        new(false, _current, _current, Releases, null, message, status, mode);

    private static void ValidateResponseUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com"))
            throw new InvalidDataException("O servidor redirecionou o pacote para um endereço não permitido.");
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A pasta de atualização contém um redirecionamento de arquivos não permitido.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
