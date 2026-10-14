using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Security;

public sealed class WindowsSecurityService(
    IPowerShellExecutor powerShell,
    ILogger<WindowsSecurityService> logger) : ISecurityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<SecuritySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await powerShell.ExecuteAsync("""
                $defender = $null
                $firewall = @()
                $bitlocker = @()
                $tpm = $null
                $secureBoot = $null
                $admins = @()
                try { $defender = Get-MpComputerStatus -ErrorAction Stop } catch { }
                try { $firewall = @(Get-NetFirewallProfile -ErrorAction Stop) } catch { }
                try { $bitlocker = @(Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop) } catch { }
                try { $tpm = Get-Tpm -ErrorAction Stop } catch { }
                try { $secureBoot = [bool](Confirm-SecureBootUEFI -ErrorAction Stop) } catch { }
                try { $admins = @(Get-LocalGroupMember -Group 'Administradores' -ErrorAction Stop) } catch {
                    try { $admins = @(Get-LocalGroupMember -Group 'Administrators' -ErrorAction Stop) } catch { }
                }
                [pscustomobject]@{
                    DefenderEnabled = $(if ($null -eq $defender) { $null } else { [bool]$defender.AntivirusEnabled })
                    RealTimeProtectionEnabled = $(if ($null -eq $defender) { $null } else { [bool]$defender.RealTimeProtectionEnabled })
                    FirewallEnabled = $(if ($firewall.Count -eq 0) { $null } else { @($firewall | Where-Object { -not $_.Enabled }).Count -eq 0 })
                    BitLockerEnabled = $(if ($bitlocker.Count -eq 0) { $null } else { @($bitlocker | Where-Object { $_.ProtectionStatus -eq 1 }).Count -gt 0 })
                    TpmPresent = $(if ($null -eq $tpm) { $null } else { [bool]$tpm.TpmPresent })
                    SecureBootEnabled = $secureBoot
                    LocalAdministratorCount = @($admins).Count
                } | ConvertTo-Json -Compress
                """, cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Deserialize<SecurityPayload>(json, JsonOptions);
            if (payload is null)
            {
                return new SecuritySnapshot(null, null, null, null, null, null, 0,
                    ["O Windows não retornou dados de segurança válidos."], "Resposta inválida do PowerShell.");
            }

            var warnings = new List<string>();
            if (payload.DefenderEnabled == false) warnings.Add("Microsoft Defender não está ativo.");
            if (payload.RealTimeProtectionEnabled == false) warnings.Add("A proteção em tempo real está desativada.");
            if (payload.FirewallEnabled == false) warnings.Add("Um ou mais perfis do firewall estão desativados.");
            if (payload.BitLockerEnabled == false) warnings.Add("A proteção BitLocker da unidade do sistema não está ativa.");
            if (payload.TpmPresent == false) warnings.Add("TPM não detectado.");
            if (payload.SecureBootEnabled == false) warnings.Add("Secure Boot não está ativo.");
            if (new bool?[] { payload.DefenderEnabled, payload.RealTimeProtectionEnabled, payload.FirewallEnabled, payload.BitLockerEnabled, payload.TpmPresent, payload.SecureBootEnabled }.Any(value => value is null))
                warnings.Add("Há controles não verificados. Permissões insuficientes ou recursos indisponíveis não significam proteção desativada.");

            return new SecuritySnapshot(
                payload.DefenderEnabled,
                payload.RealTimeProtectionEnabled,
                payload.FirewallEnabled,
                payload.BitLockerEnabled,
                payload.TpmPresent,
                payload.SecureBootEnabled,
                payload.LocalAdministratorCount,
                warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao consultar o estado de segurança do Windows.");
            return new SecuritySnapshot(null, null, null, null, null, null, 0,
                ["Não foi possível ler todos os controles de segurança."], exception.Message);
        }
    }

    public ActionRequest CreateActionRequest(SecurityActionKind action) => action switch
    {
        SecurityActionKind.QuickDefenderScan => new ActionRequest(
            "security.defender.quick-scan",
            "Executar varredura rápida do Microsoft Defender",
            "Inicia uma varredura rápida local no Microsoft Defender. O resultado será registrado pelo Windows.",
            ActionRisk.Elevated,
            true,
            ["Microsoft Defender", "Arquivos do computador"],
            async (cancellationToken, progress) =>
            {
                progress?.Report(new ActionProgress("Iniciando a varredura rápida do Defender...", 10));
                var output = await powerShell.ExecuteAsync("Start-MpScan -ScanType QuickScan -ErrorAction Stop; 'Varredura solicitada ao Microsoft Defender.'", cancellationToken).ConfigureAwait(false);
                progress?.Report(new ActionProgress("Varredura rápida solicitada.", 100));
                return new ActionResult(true, output.Trim(), TimeSpan.Zero);
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Ação de segurança não suportada.")
    };

    private sealed record SecurityPayload(
        bool? DefenderEnabled,
        bool? RealTimeProtectionEnabled,
        bool? FirewallEnabled,
        bool? BitLockerEnabled,
        bool? TpmPresent,
        bool? SecureBootEnabled,
        int LocalAdministratorCount);
}
