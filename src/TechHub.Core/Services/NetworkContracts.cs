using TechHub.Core.Models;

namespace TechHub.Core.Services;

/// <summary>Optional extension of the original scanner contract. No scan occurs during inventory.</summary>
public interface INetworkTopologyScanner : INetworkScanner
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default);
    Task<NetworkScanResult> ScanAsync(NetworkAdapterInfo adapter,
        IProgress<ActionProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Injectable I/O boundary for deterministic tests that never contact the network.</summary>
public interface INetworkDiscoveryBackend
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken);
    Task<NetworkProbeResult> ProbeAsync(string address, CancellationToken cancellationToken);
    Task<string> ReadArpAsync(NetworkAdapterInfo adapter, CancellationToken cancellationToken);
    Task<string> ResolveNameAsync(string address, CancellationToken cancellationToken);
}
