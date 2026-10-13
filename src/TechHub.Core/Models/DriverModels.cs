namespace TechHub.Core.Models;

public sealed record DriverInfo(
    string DeviceId, string DeviceName, string? Manufacturer, string? DeviceClass,
    string? InfName, string? Provider, string? Version, DateTimeOffset? DriverDate,
    bool? IsSigned, string? Signer, uint? ProblemCode, string? Status)
{
    public bool HasProblem => ProblemCode is > 0 ||
        string.Equals(Status, "Error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "Degraded", StringComparison.OrdinalIgnoreCase);
}

// Success=true com Devices vazio é um inventário vazio, não uma falha de WMI.
public sealed record DriverInventoryResult(
    bool Success, IReadOnlyList<DriverInfo> Devices, string Message, string? Error = null);

public sealed record DriverFileVerification(
    string FilePath, string Sha256, string SignatureStatus, string? Signer)
{
    public bool HasValidSignature => SignatureStatus == "Valid";
}

// O CAT assinado é verificado aqui; o Windows verifica a associação dos arquivos
// ao catálogo ao importar o pacote com PnPUtil.
public sealed record DriverVerificationResult(
    bool Success, bool IsTrusted, IReadOnlyList<DriverFileVerification> Files,
    string Message, string? Error = null);
