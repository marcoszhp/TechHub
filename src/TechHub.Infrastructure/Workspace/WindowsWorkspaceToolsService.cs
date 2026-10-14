using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Workspace;

public sealed class WindowsWorkspaceToolsService(IAuditStore audit) : IWorkspaceToolsService
{
    public const int OutputLimit = 32_768;
    public static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(15);
    public IReadOnlyList<WorkspaceTool> Tools { get; } = Array.AsReadOnly(new[]
    {
        new WorkspaceTool("taskmgr", "Gerenciador de tarefas", "Processos e desempenho"),
        new WorkspaceTool("eventvwr", "Visualizador de eventos", "Logs do Windows"),
        new WorkspaceTool("devmgmt", "Gerenciador de dispositivos", "Hardware e drivers"),
        new WorkspaceTool("services", "Serviços", "Serviços do Windows"),
        new WorkspaceTool("diskmgmt", "Gerenciamento de disco", "Volumes e armazenamento"),
        new WorkspaceTool("resmon", "Monitor de recursos", "Uso de CPU, memória e disco")
    });

    public void Launch(string toolId)
    {
        var info = CreateToolStartInfo(toolId);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("O Windows não iniciou a ferramenta.");
    }

    public static ProcessStartInfo CreateToolStartInfo(string toolId)
    {
        var info = new ProcessStartInfo { UseShellExecute = false };
        switch (toolId)
        {
            case "taskmgr": case "resmon":
                info.FileName = Path.Combine(Environment.SystemDirectory, toolId + ".exe");
                break;
            case "eventvwr": case "devmgmt": case "services": case "diskmgmt":
                info.FileName = Path.Combine(Environment.SystemDirectory, "mmc.exe");
                info.ArgumentList.Add(Path.Combine(Environment.SystemDirectory, toolId + ".msc"));
                break;
            default: throw new ArgumentException("Ferramenta não permitida.", nameof(toolId));
        }
        return info;
    }

    public static string ValidateHost(string? host, bool hostnameOnly = false)
    {
        var value = host?.Trim() ?? "";
        if (value.Length is 0 or > 253 || value.StartsWith('-') || value.Contains('%'))
            throw new ArgumentException("Informe somente um hostname ou endereço IP, sem opções, URL ou comandos.");
        if (IPAddress.TryParse(value, out var address))
        {
            if (hostnameOnly) throw new ArgumentException("Informe um nome DNS para nslookup.");
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast)
                || address.IsIPv6Multicast || (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224))
                throw new ArgumentException("O destino deve ser um único host unicast.");
            return address.ToString();
        }
        if (!Regex.IsMatch(value, @"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\.?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("Hostname inválido. Use letras, números, hífen e ponto.");
        return value;
    }

    public static ProcessStartInfo CreateDiagnosticStartInfo(WorkspaceDiagnostic diagnostic, string? host)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        };
        switch (diagnostic)
        {
            case WorkspaceDiagnostic.IpConfiguration:
                info.FileName = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
                info.ArgumentList.Add("/all");
                break;
            case WorkspaceDiagnostic.PingGateway: case WorkspaceDiagnostic.PingHost:
                info.FileName = Path.Combine(Environment.SystemDirectory, "ping.exe");
                foreach (var arg in new[] { "-n", "4", "-w", "1000", ValidateHost(host) }) info.ArgumentList.Add(arg);
                break;
            case WorkspaceDiagnostic.NameLookup:
                info.FileName = Path.Combine(Environment.SystemDirectory, "nslookup.exe");
                info.ArgumentList.Add("-timeout=2");
                info.ArgumentList.Add("-retry=1");
                info.ArgumentList.Add(ValidateHost(host, hostnameOnly: true));
                break;
            default: throw new ArgumentException("Diagnóstico não permitido.");
        }
        return info;
    }

    public async Task<DiagnosticCompletion> RunAsync(WorkspaceDiagnostic diagnostic, string? host,
        IProgress<string>? output = null, CancellationToken cancellationToken = default)
    {
        // Process setup, gateway enumeration and audit SQLite writes also run off the UI thread.
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic == WorkspaceDiagnostic.PingGateway) host = DefaultGateway();
            var startInfo = CreateDiagnosticStartInfo(diagnostic, host);
            var started = DateTimeOffset.UtcNow;
            var watch = Stopwatch.StartNew();
            var buffer = new StringBuilder();
            var bufferGate = new object();
            bool truncated = false, cancelled = false, timedOut = false;
            int? exitCode = null;
            string message;
            using var process = new Process { StartInfo = startInfo };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DiagnosticTimeout);
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Não foi possível iniciar o diagnóstico.");
                var stdout = PumpAsync(process.StandardOutput);
                var stderr = PumpAsync(process.StandardError);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token).ConfigureAwait(false);
                    exitCode = process.ExitCode;
                    message = exitCode == 0 ? "Diagnóstico concluído (código 0)." : $"Diagnóstico terminou com código {exitCode}. Consulte a saída.";
                }
                catch (OperationCanceledException)
                {
                    cancelled = cancellationToken.IsCancellationRequested;
                    timedOut = !cancelled;
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    // Close streams to release pending reads even if Windows refuses to terminate a process.
                    process.StandardOutput.Dispose();
                    process.StandardError.Dispose();
                    try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or TimeoutException) { }
                    message = cancelled ? "Diagnóstico cancelado." : "Tempo limite de 15 segundos atingido.";
                }
            }
            catch (Exception ex) { message = $"Falha no diagnóstico: {ex.Message}"; }
            watch.Stop();
            try
            {
                await audit.AppendAsync(new AuditEntry($"workspace.{diagnostic}", diagnostic.ToString(), Environment.UserName,
                    true, exitCode == 0 && !cancelled && !timedOut, message, started, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) { message += $" Histórico não registrado: {ex.Message}"; }
            string resultOutput;
            lock (bufferGate) resultOutput = buffer.ToString();
            if (truncated) message += " Saída limitada a 32 KB de texto.";
            return new DiagnosticCompletion(exitCode, cancelled, timedOut, resultOutput, message, watch.Elapsed);

            async Task PumpAsync(StreamReader reader)
            {
                var chunk = new char[1024];
                int read;
                while ((read = await reader.ReadAsync(chunk.AsMemory(), timeout.Token).ConfigureAwait(false)) != 0)
                {
                    lock (bufferGate)
                    {
                        var count = Math.Min(read, OutputLimit - buffer.Length);
                        if (count > 0)
                        {
                            var text = new string(chunk, 0, count);
                            buffer.Append(text);
                            output?.Report(text);
                        }
                        if (count < read) truncated = true;
                    }
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string DefaultGateway() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().GatewayAddresses)
        .Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString()
        ?? throw new InvalidOperationException("Nenhum gateway IPv4 ativo foi encontrado. Use Ping de host.");
}
