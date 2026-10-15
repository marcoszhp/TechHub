using System.Security.Cryptography;
using TechHub.Core.Services;

namespace TechHub.Core.Updates;

public sealed record UpdateJob(int ParentProcessId, long ParentStartTicks, string InstallDirectory,
    UpdateInstallMode Mode, string CurrentVersion, string NextVersion, string Sha256, long SizeBytes);
public sealed record UpdateApplyResult(bool Success, string Version, string Message, DateTimeOffset CompletedAt);

public static class UpdatePolicy
{
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    public static string StagingRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechHub", "Updates");
    public static string DefaultDirectory(UpdateInstallMode mode) => mode switch
    {
        UpdateInstallMode.User => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechHub"),
        UpdateInstallMode.Machine => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TechHub"),
        _ => throw new InvalidDataException("A modalidade não aceita atualização integrada.")
    };
    public static string PackageName(UpdateJob job) => $"TechHub-{(job.Mode == UpdateInstallMode.User ? "User-" : "")}Setup-{job.NextVersion}.exe";
    public static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    public static Version ParseVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 || parsed.ToString(3) != version)
            throw new InvalidDataException("Versão inválida: esperado major.minor.patch.");
        return parsed;
    }

    public static void RejectReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.AsSpan(2).Contains(':'))
            throw new InvalidDataException("Caminho de atualização inválido.");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links e redirecionamentos não são aceitos na atualização.");
    }

    public static void Validate(UpdateJob job, string directory)
    {
        if (job.ParentProcessId <= 0 || job.ParentStartTicks <= 0 ||
            ParseVersion(job.NextVersion) <= ParseVersion(job.CurrentVersion) ||
            job.SizeBytes <= 0 || job.SizeBytes > MaximumPackageBytes ||
            job.Sha256.Length != 64 || !job.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Contrato da atualização inválido.");
        if (!SamePath(job.InstallDirectory, DefaultDirectory(job.Mode)))
            throw new InvalidDataException("Diretório de instalação não suportado pelo atualizador.");
        if (!SamePath(Path.GetDirectoryName(Path.GetFullPath(directory))!, StagingRoot) ||
            !Guid.TryParseExact(Path.GetFileName(directory), "N", out _))
            throw new InvalidDataException("Pasta de preparação inválida.");
        RejectReparsePoints(directory);
        RejectReparsePoints(job.InstallDirectory);
        RejectReparsePoints(Path.Combine(directory, PackageName(job)));
        RejectReparsePoints(Path.Combine(job.InstallDirectory, "TechHub.App.exe"));
        if (File.Exists(Path.Combine(job.InstallDirectory, "portable.flag")) ||
            !File.Exists(Path.Combine(job.InstallDirectory, "TechHub.App.exe")) ||
            !File.Exists(Path.Combine(job.InstallDirectory, "unins000.exe")))
            throw new InvalidDataException("Instalação não reconhecida; utilize a atualização manual.");
        var marker = Path.Combine(job.InstallDirectory, "install-mode.txt");
        RejectReparsePoints(marker);
        if (File.Exists(marker) && !string.Equals(File.ReadAllText(marker).Trim(), job.Mode.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A modalidade do pacote difere da instalação.");
    }

    public static async Task VerifyPackageAsync(Stream stream, UpdateJob job, CancellationToken token)
    {
        if (stream.Length != job.SizeBytes) throw new InvalidDataException("O instalador está incompleto ou tem tamanho diferente.");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!string.Equals(hash, job.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A integridade do instalador não foi confirmada.");
        stream.Position = 0;
    }
}

/// <summary>Testable sequence; platform implementation is restricted to the fixed TechHub installer.</summary>
public interface IUpdateApplyRuntime
{
    Task WaitForParentAsync(UpdateJob job, CancellationToken token);
    Task VerifyReleaseAsync(UpdateJob job, CancellationToken token);
    void AssertNoOtherInstances();
    Task<int> InstallAsync(UpdateJob job, string directory, CancellationToken token);
    string ReadInstalledVersion(UpdateJob job);
    void Restart(UpdateJob job);
}

public sealed class UpdateApplyEngine(IUpdateApplyRuntime runtime)
{
    public async Task<UpdateApplyResult> ApplyAsync(UpdateJob job, string directory, CancellationToken token = default)
    {
        // File lock spans verification and execution, denying replacement/writes while Inno reads it.
        await using var package = new FileStream(Path.Combine(directory, UpdatePolicy.PackageName(job)),
            FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await UpdatePolicy.VerifyPackageAsync(package, job, token);
        await runtime.WaitForParentAsync(job, token);
        EnsureAuthorized(directory);
        runtime.AssertNoOtherInstances();
        await runtime.VerifyReleaseAsync(job, token);
        var exitCode = await runtime.InstallAsync(job, directory, token);
        if (exitCode != 0)
            throw new InvalidOperationException(exitCode == 3010
                ? "O Windows precisa reiniciar para concluir. Salve seu trabalho e reinicie o computador."
                : $"O instalador não concluiu (código {exitCode}). Consulte installer.log; o backup foi preservado.");
        if (runtime.ReadInstalledVersion(job) != job.NextVersion)
            throw new InvalidOperationException("A versão instalada não corresponde ao pacote. O backup foi preservado; use a reparação manual.");
        // Persist success before restart so the next process can show it. Restart failures overwrite it in the host.
        var result = new UpdateApplyResult(true, job.NextVersion, $"Atualizado para {job.NextVersion}. Seus dados foram preservados.", DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), System.Text.Json.JsonSerializer.Serialize(result), CancellationToken.None);
        runtime.Restart(job);
        return result;
    }

    public static void EnsureAuthorized(string directory)
    {
        if (File.Exists(Path.Combine(directory, "aborted")) || !File.Exists(Path.Combine(directory, "apply-authorized")))
            throw new OperationCanceledException("Esta tentativa não foi autorizada ou foi substituída. Verifique novamente no aplicativo.");
    }
}
