namespace TechHub.Core.Models;

public sealed record NetworkDeviceInfo(
    string IpAddress,
    string MacAddress,
    string Manufacturer,
    string HostName,
    string DeviceType,
    bool IsReachable,
    int? LatencyMilliseconds,
    DateTimeOffset DiscoveredAt)
{
    public NetworkDeviceRole Role { get; init; } = NetworkDeviceRole.Discovered;
    public bool WasProbed { get; init; }
    public string Evidence { get; init; } = "";
}

public enum NetworkDeviceRole { Discovered, Local, Gateway }

/// <summary>One active IPv4 configuration; an adapter can have more than one address.</summary>
public sealed record NetworkAdapterInfo(
    string Id, string Name, string LocalAddress, int PrefixLength,
    string? GatewayAddress, string MacAddress)
{
    public string DisplayName => $"{Name} · {LocalAddress}/{PrefixLength}";
    public string Cidr => Services.NetworkAddressing.GetNetworkCidr(LocalAddress, PrefixLength);
    public string ScanCidr => Services.NetworkAddressing.GetNetworkCidr(LocalAddress, Math.Max(24, PrefixLength));
}

public sealed record NetworkScanResult(
    IReadOnlyList<NetworkDeviceInfo> Devices, IReadOnlyList<string> Warnings,
    string ScanCidr, int AttemptedAddresses);

public sealed record NetworkProbeResult(bool IsReachable, int? LatencyMilliseconds);
