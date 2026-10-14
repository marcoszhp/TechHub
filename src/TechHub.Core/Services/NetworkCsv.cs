using System.Globalization;
using System.Text;
using TechHub.Core.Models;

namespace TechHub.Core.Services;

public static class NetworkCsv
{
    public static string Escape(string? value)
    {
        value ??= "";
        // Protect spreadsheet imports, including formulas hidden behind whitespace/control characters.
        var significant = value.TrimStart();
        if (significant.Length > 0 && "=+-@".Contains(significant[0]) || value.Any(c => c is '\t' or '\r' or '\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string Serialize(IEnumerable<NetworkDeviceInfo> devices)
    {
        var csv = new StringBuilder("IP,MAC,Nome,Tipo,Fabricante_OUI_indicio,Resposta_ICMP,Latencia_ms,Evidencia,Observado_em\r\n");
        foreach (var device in devices)
        {
            var fields = new[] { device.IpAddress, device.MacAddress, device.HostName, device.DeviceType,
                device.Manufacturer, device.WasProbed ? (device.IsReachable ? "Sim" : "Sem resposta") : "Não consultado",
                device.LatencyMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                device.Evidence, device.DiscoveredAt.ToString("O", CultureInfo.InvariantCulture) };
            csv.AppendLine(string.Join(",", fields.Select(Escape)));
        }
        return csv.ToString();
    }
}
