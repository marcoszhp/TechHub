namespace TechHub.Core.Models;

public sealed record NetworkDeviceInfo(
    string IpAddress,
    string MacAddress,
    string Manufacturer,
    string HostName,
    string DeviceType,
    bool IsReachable,
    int? LatencyMilliseconds,
    DateTimeOffset DiscoveredAt);
