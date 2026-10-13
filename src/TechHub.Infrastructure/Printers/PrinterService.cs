using System.Management;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Printers;

public sealed class PrinterService(IPowerShellExecutor powerShellExecutor) : IPrinterService
{
    public Task<IReadOnlyList<PrinterInfo>> GetInventoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<PrinterInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new List<PrinterInfo>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Status, DriverName, PortName, Default, Network, ComputerName FROM Win32_Printer");
            foreach (ManagementObject printer in searcher.Get())
            {
                result.Add(new PrinterInfo(
                    Convert.ToString(printer["Name"]) ?? "Sem nome",
                    Convert.ToString(printer["Status"]) ?? "Desconhecido",
                    Convert.ToString(printer["DriverName"]) ?? "Desconhecido",
                    Convert.ToString(printer["PortName"]) ?? "Desconhecida",
                    Convert.ToBoolean(printer["Default"] ?? false),
                    Convert.ToBoolean(printer["Network"] ?? false),
                    Convert.ToString(printer["ComputerName"])));
            }

            return result.OrderBy(item => item.Name).ToArray();
        }, cancellationToken);

    public ActionRequest CreateInstallByIpRequest(string ipAddress, string? printerName = null)
    {
        var normalizedIp = ipAddress.Trim();
        var name = string.IsNullOrWhiteSpace(printerName) ? $"TechHub-{normalizedIp}" : printerName.Trim();
        return new ActionRequest(
            "printer.install-by-ip",
            "Instalar impressora por IP",
            $"Criar uma porta TCP/IP para {normalizedIp} e instalar a impressora '{name}'.",
            ActionRisk.Elevated,
            true,
            [$"Impressora: {name}", $"Endereço: {normalizedIp}"],
            (cancellationToken, progress) => InstallByIpAsync(normalizedIp, name, progress, cancellationToken));
    }

    private async Task<ActionResult> InstallByIpAsync(
        string ipAddress,
        string printerName,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!System.Net.IPAddress.TryParse(ipAddress, out _))
        {
            return ActionResult.Failed("Informe um endereço IPv4 válido.");
        }

        var portName = $"IP_{ipAddress.Replace('.', '_')}";
        var safeIp = EscapePowerShell(ipAddress);
        var safePort = EscapePowerShell(portName);
        var safeName = EscapePowerShell(printerName);
        var script =
            "$ErrorActionPreference = 'Stop'\n" +
            $"$port = Get-PrinterPort -Name '{safePort}' -ErrorAction SilentlyContinue\n" +
            $"if ($null -eq $port) {{ Add-PrinterPort -Name '{safePort}' -PrinterHostAddress '{safeIp}' }}\n" +
            $"$printer = Get-Printer -Name '{safeName}' -ErrorAction SilentlyContinue\n" +
            $"if ($null -eq $printer) {{ Add-Printer -Name '{safeName}' -DriverName 'Microsoft IPP Class Driver' -PortName '{safePort}' }}\n" +
            "'Impressora configurada com sucesso.'\n";

        progress?.Report(new ActionProgress("Configurando a porta {portName}", 30));
        var output = await powerShellExecutor.ExecuteAsync(script, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ActionProgress("Instalação concluída", 100));
        return new ActionResult(true, string.IsNullOrWhiteSpace(output) ? "Impressora instalada." : output.Trim(), TimeSpan.Zero);
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
