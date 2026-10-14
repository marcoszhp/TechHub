using System.Net;
using System.Net.Sockets;
using TechHub.Core.Models;

namespace TechHub.Core.Services;

public static class NetworkAddressing
{
    public static uint ToUInt32(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Endereço IPv4 inválido.", nameof(address));
        var bytes = ip.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    public static string FromUInt32(uint address) =>
        new IPAddress(new byte[] { (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address }).ToString();

    private static uint Mask(int prefix)
    {
        if (prefix is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(prefix));
        return prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
    }

    public static string GetNetworkCidr(string local, int prefix) => $"{FromUInt32(ToUInt32(local) & Mask(prefix))}/{prefix}";

    public static bool IsUnicast(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var first = ip.GetAddressBytes()[0];
        return first is > 0 and < 224 && first != 127;
    }

    public static bool IsHostInSubnet(string address, NetworkAdapterInfo adapter)
    {
        if (!IsUnicast(address)) return false;
        var value = ToUInt32(address);
        var mask = Mask(adapter.PrefixLength);
        var network = ToUInt32(adapter.LocalAddress) & mask;
        if ((value & mask) != network) return false;
        // RFC 3021 point-to-point /31 and a /32 do not have broadcast addresses.
        return adapter.PrefixLength >= 31 || (value != network && value != (network | ~mask));
    }

    /// <summary>At most 254 candidates, in the /24 containing the local IP or the narrower actual subnet.</summary>
    public static IReadOnlyList<string> GetScanAddresses(NetworkAdapterInfo adapter)
    {
        if (!IsHostInSubnet(adapter.LocalAddress, adapter))
            throw new ArgumentException("O IP local não é um host IPv4 válido para esta rede.", nameof(adapter));
        var local = ToUInt32(adapter.LocalAddress);
        var prefix = Math.Max(24, adapter.PrefixLength);
        var mask = Mask(prefix);
        var first = local & mask;
        var last = first | ~mask;
        var addresses = new List<string>(254);
        // ulong prevents wraparound at the top of IPv4 space.
        for (ulong value = first; value <= last; value++)
        {
            if (prefix <= 30 && (value == first || value == last)) continue;
            if (value == local) continue;
            var address = FromUInt32((uint)value);
            if (IsHostInSubnet(address, adapter)) addresses.Add(address);
        }
        return addresses;
    }
}
