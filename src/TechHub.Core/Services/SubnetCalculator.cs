using System.Net;
using TechHub.Core.Models;

namespace TechHub.Core.Services;

public sealed class SubnetCalculator : ISubnetCalculator
{
    public SubnetCalculation Calculate(string cidr)
    {
        var parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            throw new FormatException("Informe uma rede IPv4 no formato endereço/prefixo, por exemplo 192.168.1.0/24.");
        var bytes = address.GetAddressBytes();
        var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network = value & mask;
        var broadcast = network | ~mask;
        var first = prefix >= 31 ? network : network + 1;
        var last = prefix >= 31 ? broadcast : broadcast - 1;
        var hosts = prefix == 32 ? 1 : prefix == 31 ? 2 : (long)Math.Pow(2, 32 - prefix) - 2;
        return new SubnetCalculation(ToIp(network), ToIp(broadcast), ToIp(first), ToIp(last), hosts, prefix);
    }

    private static string ToIp(uint value) => string.Join('.', (value >> 24) & 255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
}
