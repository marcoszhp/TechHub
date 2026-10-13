using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Network;

public sealed class NetworkScanner(INetworkConsentStore consentStore) : INetworkScanner
{
    private static readonly Regex ArpLine = new(
        @"(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-fA-F-]{11,17})\s+(?<type>\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<NetworkDeviceInfo>> ScanAsync(
        IProgress<ActionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await consentStore.HasConsentAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A varredura exige consentimento explícito para a rede local.");
        }

        var subnet = GetActiveSubnet();
        var addresses = subnet.HostAddresses.ToArray();
        var reachable = new List<NetworkDeviceInfo>();
        using var semaphore = new SemaphoreSlim(32);
        var completed = 0;
        var tasks = addresses.Select(async address =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var ping = new Ping();
                var started = Stopwatch.GetTimestamp();
                var reply = await ping.SendPingAsync(address, 800).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    var latency = (int)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    lock (reachable)
                    {
                        reachable.Add(new NetworkDeviceInfo(
                            address.ToString(),
                            string.Empty,
                            "Fabricante desconhecido",
                            string.Empty,
                            "Dispositivo de rede",
                            true,
                            latency,
                            DateTimeOffset.Now));
                    }
                }
            }
            catch (PingException)
            {
                // Host indisponível não é uma falha da varredura.
            }
            finally
            {
                semaphore.Release();
                var current = Interlocked.Increment(ref completed);
                progress?.Report(new ActionProgress($"Verificando {current}/{addresses.Length}", current * 100d / addresses.Length));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var arp = await ReadArpTableAsync(cancellationToken).ConfigureAwait(false);
        var byIp = reachable.ToDictionary(item => item.IpAddress, StringComparer.OrdinalIgnoreCase);
        foreach (var arpEntry in arp)
        {
            if (byIp.TryGetValue(arpEntry.IpAddress, out var device))
            {
                byIp[arpEntry.IpAddress] = device with
                {
                    MacAddress = arpEntry.MacAddress,
                    Manufacturer = OuiResolver.Resolve(arpEntry.MacAddress),
                    DeviceType = InferDeviceType(OuiResolver.Resolve(arpEntry.MacAddress))
                };
            }
            else
            {
                byIp[arpEntry.IpAddress] = new NetworkDeviceInfo(
                    arpEntry.IpAddress,
                    arpEntry.MacAddress,
                    OuiResolver.Resolve(arpEntry.MacAddress),
                    string.Empty,
                    InferDeviceType(OuiResolver.Resolve(arpEntry.MacAddress)),
                    false,
                    null,
                    DateTimeOffset.Now);
            }
        }

        return byIp.Values.OrderBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static (IPAddress[] HostAddresses, IPAddress LocalAddress) GetActiveSubnet()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                                       !IPAddress.IsLoopback(address.Address) &&
                                       !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
        if (active is null) throw new InvalidOperationException("Nenhuma interface IPv4 ativa foi encontrada.");

        var ipBytes = active.Address.GetAddressBytes();
        var maskBytes = active.IPv4Mask.GetAddressBytes();
        var networkBytes = new byte[4];
        var broadcastBytes = new byte[4];
        for (var index = 0; index < 4; index++)
        {
            networkBytes[index] = (byte)(ipBytes[index] & maskBytes[index]);
            broadcastBytes[index] = (byte)(networkBytes[index] | ~maskBytes[index]);
        }

        var start = BitConverter.ToUInt32(networkBytes.Reverse().ToArray());
        var end = BitConverter.ToUInt32(broadcastBytes.Reverse().ToArray());
        var hosts = new List<IPAddress>();
        for (var value = start + 1; value < end && hosts.Count < 254; value++)
        {
            var bytes = BitConverter.GetBytes(value).Reverse().ToArray();
            var address = new IPAddress(bytes);
            if (!address.Equals(active.Address)) hosts.Add(address);
        }

        return (hosts.ToArray(), active.Address);
    }

    private static async Task<IReadOnlyList<(string IpAddress, string MacAddress)>> ReadArpTableAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "arp.exe",
            Arguments = "-a",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Não foi possível consultar a tabela ARP.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return ArpLine.Matches(output)
            .Select(match => (match.Groups["ip"].Value, match.Groups["mac"].Value))
            .Where(item => !item.Item1.EndsWith(".255", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
    }

    private static string InferDeviceType(string manufacturer) => manufacturer switch
    {
        "Apple" => "Computador ou celular",
        "Raspberry Pi" => "IoT ou computador",
        "Cisco" => "Rede",
        _ => "Dispositivo de rede"
    };
}
