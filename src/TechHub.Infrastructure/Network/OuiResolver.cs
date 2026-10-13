namespace TechHub.Infrastructure.Network;

public static class OuiResolver
{
    private static readonly IReadOnlyDictionary<string, string> Vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["001B21"] = "Intel",
        ["3C5A37"] = "Google",
        ["B827EB"] = "Raspberry Pi",
        ["F4F5D8"] = "Microsoft",
        ["ACDE48"] = "Apple",
        ["001C42"] = "Parallels",
        ["0024E4"] = "Cisco",
        ["001E8C"] = "ASUSTek",
        ["001A2B"] = "Dell"
    };

    public static string Resolve(string macAddress)
    {
        var prefix = new string(macAddress.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant();
        return Vendors.TryGetValue(prefix, out var vendor) ? vendor : "Fabricante desconhecido";
    }
}
