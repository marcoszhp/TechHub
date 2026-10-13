using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Management;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Drivers;

public sealed class DriverService(IPowerShellExecutor powerShellExecutor, IActionRunner actionRunner) : IDriverService
{
    private readonly ConditionalWeakTable<ActionRequest, object> ownedRequests = new();
    private readonly AsyncLocal<object?> executionScope = new();

    public Task<DriverInventoryResult> GetInventoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var drivers = new Dictionary<string, DriverInfo>(StringComparer.OrdinalIgnoreCase);
                Query("SELECT DeviceID, DeviceName, Manufacturer, DeviceClass, InfName, DriverProviderName, DriverVersion, DriverDate, IsSigned, Signer FROM Win32_PnPSignedDriver", item =>
                {
                    var id = Text(item, "DeviceID");
                    if (string.IsNullOrWhiteSpace(id)) return;
                    var date = Text(item, "DriverDate");
                    drivers[id] = new DriverInfo(id, Text(item, "DeviceName") ?? "Dispositivo sem nome",
                        Text(item, "Manufacturer"), Text(item, "DeviceClass"), Text(item, "InfName"),
                        Text(item, "DriverProviderName"), Text(item, "DriverVersion"),
                        string.IsNullOrWhiteSpace(date) ? null : new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(date)),
                        item["IsSigned"] is bool signed ? signed : null, Text(item, "Signer"), null, null);
                }, cancellationToken);
                Query("SELECT DeviceID, Name, Manufacturer, PNPClass, ConfigManagerErrorCode, Status FROM Win32_PnPEntity", item =>
                {
                    var id = Text(item, "DeviceID");
                    if (string.IsNullOrWhiteSpace(id)) return;
                    var code = item["ConfigManagerErrorCode"] is null ? (uint?)null : Convert.ToUInt32(item["ConfigManagerErrorCode"], CultureInfo.InvariantCulture);
                    if (!drivers.TryGetValue(id, out var driver))
                        driver = new DriverInfo(id, Text(item, "Name") ?? "Dispositivo sem nome", Text(item, "Manufacturer"),
                            Text(item, "PNPClass"), null, null, null, null, null, null, null, null);
                    drivers[id] = driver with { ProblemCode = code, Status = Text(item, "Status") };
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var result = drivers.Values.OrderBy(x => x.DeviceName, StringComparer.CurrentCultureIgnoreCase).ToArray();
                return new DriverInventoryResult(true, Array.AsReadOnly(result),
                    result.Length == 0 ? "Nenhum dispositivo encontrado." : $"Inventário concluído: {result.Length} dispositivos.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                return new DriverInventoryResult(false, Array.Empty<DriverInfo>(), "Não foi possível consultar o inventário de drivers.", exception.Message);
            }
        }, cancellationToken);

    public async Task<DriverVerificationResult> VerifyOfflineDriverAsync(string infPath, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = LocalPath(infPath);
            if (!Path.GetExtension(path).Equals(".inf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Selecione um arquivo INF local.");
            var content = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            var catalogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inVersion = false;
            foreach (var raw in content)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = raw.Split(';', 2)[0].Trim();
                if (line.StartsWith('[')) { inVersion = line.Equals("[Version]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inVersion) continue;
                var match = Regex.Match(line, @"^CatalogFile(?:\.[A-Za-z0-9_.]+)?\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var name = match.Groups[1].Value.Trim().Trim('"');
                if (name.Length == 0 || name != Path.GetFileName(name) || name.Contains('%') ||
                    !Path.GetExtension(name).Equals(".cat", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("O INF contém uma referência de catálogo inválida ou não resolvida.");
                catalogs.Add(LocalPath(Path.Combine(Path.GetDirectoryName(path)!, name)));
            }
            var files = new List<DriverFileVerification> { await VerifyFileAsync(path, cancellationToken).ConfigureAwait(false) };
            foreach (var catalog in catalogs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                files.Add(await VerifyFileAsync(catalog, cancellationToken).ConfigureAwait(false));
            var trusted = catalogs.Count > 0 ? files.Skip(1).All(x => x.HasValidSignature) : files[0].HasValidSignature;
            return new DriverVerificationResult(true, trusted, files.AsReadOnly(), trusted
                ? "Assinatura válida e hashes SHA-256 calculados. A associação ao catálogo será validada pelo Windows na importação."
                : "O pacote não possui uma assinatura válida e confiável; a instalação foi bloqueada.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            return new DriverVerificationResult(false, false, Array.Empty<DriverFileVerification>(),
                "Não foi possível verificar o pacote de driver local.", exception.Message);
        }
    }

    public ActionRequest CreateBackupRequest(string publishedInfName, string destinationDirectory)
    {
        if (publishedInfName is null || !Regex.IsMatch(publishedInfName, @"^oem[0-9]+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Informe um INF publicado específico, como oem42.inf.", nameof(publishedInfName));
        var inf = publishedInfName.ToLowerInvariant();
        var destination = LocalPath(destinationDirectory);
        return CreateRequest("driver.backup", "Backup de driver", $"Exportar somente {inf} para {destination}.",
            [$"Driver publicado: {inf}", $"Diretório de backup: {destination}"], async (ct, progress) =>
            {
                ct.ThrowIfCancellationRequested();
                LocalPath(destination);
                if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", inf)))
                    return ActionResult.Failed("O driver publicado não foi encontrado no Windows.");
                // Não sobrescreve backups existentes nem permite exportação geral com '*'.
                if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                    return ActionResult.Failed("Escolha um diretório de backup novo ou vazio.");
                Directory.CreateDirectory(destination);
                progress?.Report(new ActionProgress("Exportando o pacote de driver", 20));
                return await RunPnPUtilAsync(["/export-driver", inf, destination], false, ct).ConfigureAwait(false);
            });
    }

    public async Task<ActionRequest> CreateOfflineInstallRequestAsync(string infPath, CancellationToken cancellationToken = default)
    {
        var verification = await VerifyOfflineDriverAsync(infPath, cancellationToken).ConfigureAwait(false);
        if (!verification.Success || !verification.IsTrusted)
            throw new InvalidOperationException(verification.Message + (verification.Error is null ? "" : $" {verification.Error}"));
        var path = verification.Files[0].FilePath;
        return CreateRequest("driver.install-offline", "Instalar driver offline",
            $"Importar {path} e instalar nos dispositivos compatíveis. Não força substituição nem reinicia automaticamente.",
            [$"INF local: {path}", $"Pacote local: {Path.GetDirectoryName(path)}",
                "Driver Store do Windows", "Todos os dispositivos compatíveis com os IDs declarados neste INF"], async (ct, progress) =>
            {
                progress?.Report(new ActionProgress("Revalidando assinatura e integridade do pacote", 10));
                var locks = new List<FileStream>();
                try
                {
                // Preserva INF e CAT da prévia até o término do PnPUtil, evitando
                // troca de conteúdo entre a revalidação e a importação.
                foreach (var file in verification.Files)
                    locks.Add(new FileStream(LocalPath(file.FilePath), FileMode.Open, FileAccess.Read, FileShare.Read));
                var current = await VerifyOfflineDriverAsync(path, ct).ConfigureAwait(false);
                if (!current.Success || !current.IsTrusted)
                    return ActionResult.Failed(current.Message);
                if (current.Files.Count != verification.Files.Count || current.Files.Where((file, i) =>
                        file.FilePath != verification.Files[i].FilePath || file.Sha256 != verification.Files[i].Sha256).Any())
                    return ActionResult.Failed("O INF ou catálogo mudou desde a prévia. Crie uma nova solicitação.");
                progress?.Report(new ActionProgress("Importando o INF e instalando nos dispositivos compatíveis", 40));
                return await RunPnPUtilAsync(["/add-driver", path, "/install"], true, ct).ConfigureAwait(false);
                }
                finally { foreach (var handle in locks) handle.Dispose(); }
            });
    }

    public async Task<ActionResult> ExecuteAsync(ActionRequest request, ActionExecutionContext context,
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ownedRequests.TryGetValue(request, out var scope))
            return ActionResult.Failed("A solicitação não pertence a este serviço de drivers.");
        var previous = executionScope.Value;
        executionScope.Value = scope;
        try { return await actionRunner.ExecuteAsync(request, context, progress, cancellationToken).ConfigureAwait(false); }
        finally { executionScope.Value = previous; }
    }

    private ActionRequest CreateRequest(string id, string title, string description, string[] resources,
        Func<CancellationToken, IProgress<ActionProgress>?, Task<ActionResult>> execute)
    {
        var scope = new object();
        var request = new ActionRequest(id, title, description, ActionRisk.Elevated, true, Array.AsReadOnly(resources), async (ct, progress) =>
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(executionScope.Value, scope))
                return ActionResult.Failed("Execute esta solicitação pelo serviço de drivers, que a encaminha ao ActionRunner.");
            try { return await execute(ct, progress).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { return ActionResult.Failed("A operação de driver falhou. Consulte os detalhes da auditoria.", exception); }
        });
        ownedRequests.Add(request, scope);
        return request;
    }

    private async Task<DriverFileVerification> VerifyFileAsync(string path, CancellationToken ct)
    {
        // Mantém o arquivo aberto sem compartilhamento de escrita/exclusão durante hash e assinatura.
        using var stream = new FileStream(LocalPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        var script = "$ErrorActionPreference = 'Stop'\n" +
            $"$s = Get-AuthenticodeSignature -LiteralPath '{path.Replace("'", "''", StringComparison.Ordinal)}' -ErrorAction Stop\n" +
            "[pscustomobject]@{ Status = $s.Status.ToString(); Signer = $(if ($s.SignerCertificate) { $s.SignerCertificate.Subject } else { $null }) } | ConvertTo-Json -Compress\n";
        var output = await powerShellExecutor.ExecuteAsync(script, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        using var json = JsonDocument.Parse(output);
        var status = json.RootElement.GetProperty("Status").GetString() ?? "UnknownError";
        var signer = json.RootElement.GetProperty("Signer").GetString();
        return new DriverFileVerification(path, hash, status, signer);
    }

    private static async Task<ActionResult> RunPnPUtilAsync(string[] arguments, bool install, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32";
        var start = new ProcessStartInfo(Path.Combine(windows, system, "pnputil.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o PnPUtil.");
        // Drena as duas saídas simultaneamente para evitar bloqueio por buffer cheio.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        var output = (await stdout.ConfigureAwait(false) + Environment.NewLine + await stderr.ConfigureAwait(false)).Trim();
        if (output.Length > 8000) output = output[..8000];
        var success = process.ExitCode == 0 || install && process.ExitCode == 3010;
        var message = success
            ? install ? "Pacote importado; o Windows aplicou o driver onde elegível." : "Backup do driver concluído."
            : $"O PnPUtil falhou (código {process.ExitCode}).";
        if (process.ExitCode == 3010) message += " É necessário reiniciar o Windows.";
        return new ActionResult(success, message + (output.Length == 0 ? "" : $"\n{output}"), TimeSpan.Zero);
    }

    private static string LocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant) ||
            value.IndexOfAny(['*', '?', '\r', '\n', '\0']) >= 0 || value[2..].Contains(':'))
            throw new ArgumentException("Informe um caminho absoluto local, sem URL, rede, curingas ou fluxos alternativos.");
        var path = Path.GetFullPath(value);
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new ArgumentException("Caminhos em unidades de rede não são permitidos.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Links e redirecionamentos de caminho não são permitidos.");
        return path;
    }

    private static void Query(string query, Action<ManagementObject> consume, CancellationToken ct)
    {
        using var searcher = new ManagementObjectSearcher("root\\CIMV2", query,
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = true });
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item) { ct.ThrowIfCancellationRequested(); consume(item); }
        }
    }

    private static string? Text(ManagementObject item, string property) => Convert.ToString(item[property], CultureInfo.InvariantCulture);
}
