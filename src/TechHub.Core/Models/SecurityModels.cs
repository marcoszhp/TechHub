namespace TechHub.Core.Models;

public sealed record SecuritySnapshot(
    bool DefenderEnabled,
    bool RealTimeProtectionEnabled,
    bool FirewallEnabled,
    bool BitLockerEnabled,
    bool TpmPresent,
    bool SecureBootEnabled,
    int LocalAdministratorCount,
    IReadOnlyList<string> Warnings,
    string? Error = null)
{
    public bool IsHealthy => Error is null && Warnings.Count == 0;
}

public enum SecurityActionKind
{
    QuickDefenderScan
}
