using Moq;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Infrastructure.Network;

namespace TechHub.Tests;

public sealed class NetworkTopologyTests
{
    private static NetworkAdapterInfo Adapter(int prefix = 30, string local = "192.168.10.1") =>
        new("adapter", "Ethernet", local, prefix, "192.168.10.2", "00-1B-21-01-02-03");

    [Theory]
    [InlineData(16, "192.168.10.1", "192.168.10.0/24", 253)]
    [InlineData(24, "192.168.10.1", "192.168.10.0/24", 253)]
    [InlineData(30, "192.168.10.1", "192.168.10.0/30", 1)]
    [InlineData(31, "192.168.10.0", "192.168.10.0/31", 1)]
    [InlineData(32, "192.168.10.1", "192.168.10.1/32", 0)]
    public void ScanRangeIsBoundedAndExcludesLocal(int prefix, string local, string cidr, int count)
    {
        var adapter = Adapter(prefix, local);
        var addresses = NetworkAddressing.GetScanAddresses(adapter);
        Assert.Equal(cidr, adapter.ScanCidr);
        Assert.Equal(count, addresses.Count);
        Assert.DoesNotContain(local, addresses);
        Assert.All(addresses, ip => Assert.True(NetworkAddressing.IsHostInSubnet(ip, adapter)));
        Assert.Equal(addresses.Count, addresses.Distinct().Count());
    }

    [Theory]
    [InlineData("192.168.10.0", false)]
    [InlineData("192.168.10.255", false)]
    [InlineData("192.168.11.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("bad", false)]
    [InlineData("192.168.10.2", true)]
    public void SubnetRejectsNonHosts(string address, bool expected) =>
        Assert.Equal(expected, NetworkAddressing.IsHostInSubnet(address, Adapter(24)));

    [Fact]
    public void InvalidPrefixAndLocalHostAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkAddressing.GetNetworkCidr("192.168.10.1", 33));
        Assert.Throws<ArgumentException>(() => NetworkAddressing.GetScanAddresses(Adapter(24, "192.168.10.0")));
        Assert.Throws<ArgumentException>(() => NetworkAddressing.ToUInt32("::1"));
        Assert.Equal("192.168.10.1", NetworkAddressing.FromUInt32(NetworkAddressing.ToUInt32("192.168.10.1")));
    }

    [Theory]
    [InlineData("=SUM(A1)", "\"'=SUM(A1)\"")]
    [InlineData("  +cmd", "\"'  +cmd\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@name", "\"'@name\"")]
    [InlineData("a,b\"c", "\"a,b\"\"c\"")]
    [InlineData("a\nb", "\"'a\nb\"")]
    [InlineData(null, "\"\"")]
    public void CsvQuotesAndNeutralizesSpreadsheetFormulas(string? input, string expected) =>
        Assert.Equal(expected, NetworkCsv.Escape(input));

    [Fact]
    public void CsvDistinguishesUnprobedAndUnresponsiveObservations()
    {
        var observed = new NetworkDeviceInfo("192.168.10.2", "", "Desconhecido", "", "Desconhecido",
            false, null, DateTimeOffset.Parse("2026-10-14T12:00:00Z")) { Evidence = "Cache ARP" };
        var csv = NetworkCsv.Serialize([observed, observed with { WasProbed = true }]);
        Assert.StartsWith("IP,MAC,Nome,Tipo,Fabricante_OUI_indicio,Resposta_ICMP,Latencia_ms,Evidencia,Observado_em\r\n", csv);
        Assert.Contains("\"Não consultado\",\"\",\"Cache ARP\"", csv);
        Assert.Contains("\"Sem resposta\",\"\",\"Cache ARP\"", csv);
        Assert.Contains("2026-10-14T12:00:00.0000000+00:00", csv);
    }

    [Fact]
    public void ArpFiltersInterfaceSubnetInvalidMacAndDuplicates()
    {
        var text = """
            Interface: 192.168.11.1 --- 0x2
              192.168.10.2 00-1b-21-aa-bb-cc dynamic
            Interface: 192.168.10.1 --- 0x3
              192.168.10.2 00:1b:21:aa:bb:cc dynamic
              192.168.10.2 00-1b-21-aa-bb-dd dynamic
              192.168.10.3 00-1b-21-aa-bb-cc static
              192.168.11.2 00-1b-21-aa-bb-cc dynamic
              192.168.10.0 00-1b-21-aa-bb-cc dynamic
              192.168.10.255 ff-ff-ff-ff-ff-ff static
              192.168.10.4 01-00-5e-00-00-01 static
              192.168.10.5 00-00-00-00-00-00 dynamic
              999.168.10.6 00-1b-21-aa-bb-cc dynamic
            Interface: 192.168.12.1 --- 0x4
              192.168.10.6 00-1b-21-aa-bb-cc dynamic
            """;
        var entries = NetworkArpParser.Parse(text, Adapter(24));
        Assert.Equal(2, entries.Count);
        Assert.Contains(("192.168.10.2", "00-1B-21-AA-BB-DD"), entries);
        Assert.Contains(("192.168.10.3", "00-1B-21-AA-BB-CC"), entries);
        Assert.Empty(NetworkArpParser.Parse("192.168.10.2 00-1b-21-aa-bb-cc dynamic", Adapter()));
    }

    private static Mock<INetworkConsentStore> Consent(bool granted)
    {
        var mock = new Mock<INetworkConsentStore>(MockBehavior.Strict);
        mock.Setup(s => s.HasConsentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(granted);
        return mock;
    }

    [Fact]
    public async Task NoConsentPreventsAllBackendCallsForBothScanOverloads()
    {
        var backend = new Mock<INetworkDiscoveryBackend>(MockBehavior.Strict);
        var scanner = new NetworkScanner(Consent(false).Object, backend.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(Adapter()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync());
        backend.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InventoryDoesNotConsultConsentOrSendProbes()
    {
        var backend = new Mock<INetworkDiscoveryBackend>(MockBehavior.Strict);
        backend.Setup(b => b.GetAdaptersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Adapter() });
        var consent = new Mock<INetworkConsentStore>(MockBehavior.Strict);
        var scanner = new NetworkScanner(consent.Object, backend.Object);
        Assert.Single(await scanner.GetAdaptersAsync());
        backend.Verify(b => b.GetAdaptersAsync(It.IsAny<CancellationToken>()), Times.Once);
        backend.VerifyNoOtherCalls();
        consent.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangedAdapterIsRejectedBeforeTraffic()
    {
        var backend = new Mock<INetworkDiscoveryBackend>(MockBehavior.Strict);
        backend.Setup(b => b.GetAdaptersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Adapter(24) });
        var scanner = new NetworkScanner(Consent(true).Object, backend.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(Adapter()));
        backend.Verify(b => b.GetAdaptersAsync(It.IsAny<CancellationToken>()), Times.Once);
        backend.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, "Resposta ICMP + cache ARP")]
    [InlineData(false, "Cache ARP; pode estar desatualizado")]
    public async Task AuthorizedScanPreservesEvidenceAndEnrichesOnlyObservedHost(bool reachable, string evidence)
    {
        var adapter = Adapter();
        var backend = new Mock<INetworkDiscoveryBackend>(MockBehavior.Strict);
        backend.Setup(b => b.GetAdaptersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { adapter });
        backend.Setup(b => b.ProbeAsync("192.168.10.2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetworkProbeResult(reachable, reachable ? 7 : null));
        backend.Setup(b => b.ReadArpAsync(adapter, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Interface: 192.168.10.1 --- 0x3\n 192.168.10.2 00-1b-21-aa-bb-cc dynamic");
        backend.Setup(b => b.ResolveNameAsync("192.168.10.2", It.IsAny<CancellationToken>())).ReturnsAsync("host.test");
        var scanner = new NetworkScanner(Consent(true).Object, backend.Object);
        var result = await scanner.ScanAsync(adapter);
        var host = Assert.Single(result.Devices);
        Assert.Equal(evidence, host.Evidence);
        Assert.Equal(reachable, host.IsReachable);
        Assert.True(host.WasProbed);
        Assert.Equal("Intel", host.Manufacturer);
        Assert.Equal("host.test", host.HostName);
        Assert.Equal("00-1B-21-AA-BB-CC", host.MacAddress);
        Assert.Equal(1, result.AttemptedAddresses);
        Assert.Empty(result.Warnings);
        backend.Verify(b => b.ProbeAsync("192.168.10.2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancellationBeforeScanPreventsBackendCalls()
    {
        var backend = new Mock<INetworkDiscoveryBackend>(MockBehavior.Strict);
        var scanner = new NetworkScanner(Consent(true).Object, backend.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(Adapter(), cancellationToken: cancellation.Token));
        backend.VerifyNoOtherCalls();
    }
}
