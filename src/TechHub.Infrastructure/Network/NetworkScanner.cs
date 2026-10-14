using System.Collections.Concurrent;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Network;

public sealed class NetworkScanner : INetworkTopologyScanner
{
    private readonly INetworkConsentStore _consentStore;
    private readonly INetworkDiscoveryBackend _backend;

    // The existing DI registration and constructor callers remain valid.
    public NetworkScanner(INetworkConsentStore consentStore, INetworkDiscoveryBackend? backend = null)
    {
        _consentStore = consentStore;
        _backend = backend ?? new WindowsNetworkDiscoveryBackend();
    }

    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default) =>
        _backend.GetAdaptersAsync(cancellationToken);

    public async Task<IReadOnlyList<NetworkDeviceInfo>> ScanAsync(
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await RequireConsentAsync(cancellationToken).ConfigureAwait(false);
        var adapters = await GetAdaptersAsync(cancellationToken).ConfigureAwait(false);
        var adapter = adapters.FirstOrDefault() ?? throw new InvalidOperationException("Nenhum adaptador IPv4 ativo foi encontrado.");
        return (await ScanAsync(adapter, progress, cancellationToken).ConfigureAwait(false)).Devices;
    }

    public async Task<NetworkScanResult> ScanAsync(NetworkAdapterInfo adapter,
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await RequireConsentAsync(cancellationToken).ConfigureAwait(false);
        // Revalidate selection against live local configuration before sending any traffic.
        var current = (await GetAdaptersAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == adapter.Id && item.LocalAddress == adapter.LocalAddress);
        if (current is null || current.PrefixLength != adapter.PrefixLength || current.GatewayAddress != adapter.GatewayAddress)
            throw new InvalidOperationException("O adaptador mudou ou foi desconectado. Atualize os adaptadores antes de varrer.");

        var addresses = NetworkAddressing.GetScanAddresses(current);
        var candidates = addresses.ToHashSet(StringComparer.Ordinal);
        var devices = new ConcurrentDictionary<string, NetworkDeviceInfo>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var completed = 0;
        var probeErrors = 0;
        progress?.Report(new ActionProgress($"Consultando {current.ScanCidr} · {addresses.Count} IPs", 0));
        await Parallel.ForEachAsync(addresses, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                try
                {
                    var reply = await _backend.ProbeAsync(address, token).ConfigureAwait(false);
                    if (reply.IsReachable)
                        devices[address] = new NetworkDeviceInfo(address, "", "Fabricante desconhecido", "", "Tipo desconhecido",
                            true, reply.LatencyMilliseconds, DateTimeOffset.UtcNow) { WasProbed = true, Evidence = "Resposta ICMP" };
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is System.Net.NetworkInformation.PingException or System.Net.Sockets.SocketException or TimeoutException)
                {
                    Interlocked.Increment(ref probeErrors);
                }
                var count = Interlocked.Increment(ref completed);
                progress?.Report(new ActionProgress($"ICMP: {count}/{addresses.Count} IPs", count * 80d / Math.Max(1, addresses.Count)));
            }).ConfigureAwait(false);
        if (probeErrors > 0) warnings.Add($"ICMP indisponível em {probeErrors} consulta(s); firewall ou roteamento podem limitar a descoberta.");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ActionProgress("Consultando cache ARP da interface selecionada…", 82));
        try
        {
            var arpText = await _backend.ReadArpAsync(current, cancellationToken).ConfigureAwait(false);
            foreach (var entry in NetworkArpParser.Parse(arpText, current))
            {
                if (entry.IpAddress == current.LocalAddress) continue;
                if (devices.TryGetValue(entry.IpAddress, out var device))
                    devices[entry.IpAddress] = device with { MacAddress = entry.MacAddress,
                        Manufacturer = OuiResolver.Resolve(entry.MacAddress), Evidence = "Resposta ICMP + cache ARP" };
                else
                    devices[entry.IpAddress] = new NetworkDeviceInfo(entry.IpAddress, entry.MacAddress,
                        OuiResolver.Resolve(entry.MacAddress), "", "Tipo desconhecido", false, null, DateTimeOffset.UtcNow)
                    { WasProbed = candidates.Contains(entry.IpAddress), Evidence = "Cache ARP; pode estar desatualizado" };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or OperationCanceledException or TimeoutException)
        {
            warnings.Add("Cache ARP indisponível: " + ex.Message);
        }

        progress?.Report(new ActionProgress("Resolvendo nomes DNS (limite de 1 segundo por IP)…", 85));
        var dnsFailures = 0;
        var resolved = 0;
        var snapshot = devices.Values.ToArray();
        await Parallel.ForEachAsync(snapshot, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (device, token) =>
            {
                try
                {
                    var name = await _backend.ResolveNameAsync(device.IpAddress, token)
                        .WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    devices[device.IpAddress] = device with { HostName = name };
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException)
                {
                    Interlocked.Increment(ref dnsFailures);
                }
                var count = Interlocked.Increment(ref resolved);
                progress?.Report(new ActionProgress($"DNS: {count}/{snapshot.Length} IPs", 85 + count * 15d / Math.Max(1, snapshot.Length)));
            }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (dnsFailures > 0) warnings.Add($"Nome DNS indisponível para {dnsFailures} host(s). IPs e evidências foram preservados.");
        progress?.Report(new ActionProgress("Varredura concluída.", 100));
        return new NetworkScanResult(devices.Values.OrderBy(d => NetworkAddressing.ToUInt32(d.IpAddress)).ToArray(),
            warnings, current.ScanCidr, completed);
    }

    private async Task RequireConsentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await _consentStore.HasConsentAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("A varredura exige consentimento explícito para a rede local.");
        token.ThrowIfCancellationRequested();
    }
}
