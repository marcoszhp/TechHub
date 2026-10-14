using System.Globalization;
using System.Text.RegularExpressions;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Network;

public static class NetworkArpParser
{
    private static readonly Regex Header = new(@"^\s*.*?:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+---", RegexOptions.CultureInvariant);
    private static readonly Regex Entry = new(@"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>(?:[0-9a-fA-F]{2}[-:]){5}[0-9a-fA-F]{2})(?:\s|$)", RegexOptions.CultureInvariant);

    public static IReadOnlyList<(string IpAddress, string MacAddress)> Parse(string text, NetworkAdapterInfo adapter)
    {
        var selectedSection = false;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var header = Header.Match(line);
            if (header.Success)
            {
                selectedSection = header.Groups["ip"].Value == adapter.LocalAddress;
                continue;
            }
            if (!selectedSection) continue;
            var match = Entry.Match(line);
            if (!match.Success) continue;
            var ip = match.Groups["ip"].Value;
            var mac = match.Groups["mac"].Value.Replace(':', '-').ToUpperInvariant();
            if (!NetworkAddressing.IsHostInSubnet(ip, adapter)) continue;
            var bytes = mac.Split('-').Select(part => byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            if ((bytes[0] & 1) != 0 || bytes.All(value => value == 0)) continue;
            entries[ip] = mac;
        }
        return entries.Select(pair => (pair.Key, pair.Value)).ToArray();
    }
}
