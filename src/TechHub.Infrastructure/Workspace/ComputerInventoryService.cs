using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Workspace;

/// <summary>One bounded background collection at a time; no network probes or synthetic health scores.</summary>
public sealed class ComputerInventoryService : IComputerInventoryService
{
    private readonly object gate = new();
    private Task<ComputerInventory>? collection;

    public async Task<ComputerInventory> GetAsync(CancellationToken cancellationToken = default)
    {
        Task<ComputerInventory> pending;
        lock (gate)
        {
            if (collection is null || collection.IsCompleted) collection = Task.Run(Collect);
            pending = collection;
        }
        return await pending.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
    }

    private static ComputerInventory Collect()
    {
        var details = new List<ComputerDetail>
        {
            new("Computador", Environment.MachineName),
            new("Windows", RuntimeInformation.OSDescription),
            new("Arquitetura", RuntimeInformation.OSArchitecture.ToString()),
            new("Processadores lógicos", Environment.ProcessorCount.ToString())
        };
        var warnings = new List<string>();
        Read("CPU", () => Convert.ToString(Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null))?.Trim()
            ?? "Não disponível");
        Read("Memória RAM", () =>
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref memory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return $"{GiB(memory.TotalPhysical)} total · {GiB(memory.AvailablePhysical)} disponível";
        });
        try
        {
            foreach (var disk in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                Read($"Disco {disk.Name}", () => disk.IsReady
                    ? $"{GiB((ulong)disk.AvailableFreeSpace)} livres de {GiB((ulong)disk.TotalSize)} · {disk.DriveFormat}"
                    : "Não disponível");
        }
        catch (Exception ex) { warnings.Add($"Discos: {ex.Message}"); }
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                Read($"Rede · {adapter.Name}", () =>
                {
                    var properties = adapter.GetIPProperties();
                    var addresses = string.Join(", ", properties.UnicastAddresses.Select(a => a.Address.ToString()));
                    var gateways = string.Join(", ", properties.GatewayAddresses.Select(a => a.Address.ToString()));
                    return $"{adapter.OperationalStatus} · IP: {(addresses.Length == 0 ? "nenhum" : addresses)} · Gateway: {(gateways.Length == 0 ? "nenhum" : gateways)}";
                });
        }
        catch (Exception ex) { warnings.Add($"Interfaces: {ex.Message}"); }
        return new(DateTimeOffset.Now, details, warnings);

        void Read(string label, Func<string> read)
        {
            try { details.Add(new(label, read())); }
            catch (Exception ex) { details.Add(new(label, "Não disponível")); warnings.Add($"{label}: {ex.Message}"); }
        }
    }

    private static string GiB(ulong bytes) => $"{bytes / 1073741824d:N1} GB";
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
