using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Network;

public sealed class WindowsNetworkDiscoveryBackend : INetworkDiscoveryBackend
{
    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var adapters = new List<NetworkAdapterInfo>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var properties = network.GetIPProperties();
            var gateway = properties.GatewayAddresses.Select(g => g.Address)
                .FirstOrDefault(ip => NetworkAddressing.IsUnicast(ip.ToString()))?.ToString();
            var mac = string.Join("-", network.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            foreach (var unicast in properties.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var adapter = new NetworkAdapterInfo(network.Id, network.Name, unicast.Address.ToString(), unicast.PrefixLength, gateway, mac);
                if (NetworkAddressing.IsHostInSubnet(adapter.LocalAddress, adapter)) adapters.Add(adapter);
            }
        }
        return Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(adapters.OrderByDescending(a => a.GatewayAddress is not null)
            .ThenBy(a => a.Name, StringComparer.CurrentCulture).ThenBy(a => a.LocalAddress, StringComparer.Ordinal).ToArray());
    }

    public async Task<NetworkProbeResult> ProbeAsync(string address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var ping = new Ping();
        // Windows chooses the route. Discovery does not imply physical connectivity.
        var reply = await ping.SendPingAsync(IPAddress.Parse(address), 800).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new NetworkProbeResult(reply.Status == IPStatus.Success,
            reply.Status == IPStatus.Success ? (int)Math.Min(int.MaxValue, reply.RoundtripTime) : null);
    }

    public async Task<string> ReadArpAsync(NetworkAdapterInfo adapter, CancellationToken cancellationToken)
    {
        var address = NetworkAddressing.FromUInt32(NetworkAddressing.ToUInt32(adapter.LocalAddress));
        var start = new ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "arp.exe"),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add("-a");
        start.ArgumentList.Add("-N");
        start.ArgumentList.Add(address);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível consultar o ARP.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            var errorText = await error.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("ARP retornou erro: " + errorText.Trim());
            return text;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }

    public async Task<string> ResolveNameAsync(string address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        var entry = await Dns.GetHostEntryAsync(address, AddressFamily.InterNetwork, timeout.Token)
            .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        return entry.HostName == address ? "" : entry.HostName;
    }
}
