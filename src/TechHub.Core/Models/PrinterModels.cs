namespace TechHub.Core.Models;

public sealed record PrinterInfo(
    string Name,
    string Status,
    string DriverName,
    string PortName,
    bool IsDefault,
    bool IsNetwork,
    string? ComputerName = null);
