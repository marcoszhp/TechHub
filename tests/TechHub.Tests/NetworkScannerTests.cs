using TechHub.Core.Services;
using TechHub.Infrastructure.Network;

namespace TechHub.Tests;

public sealed class NetworkScannerTests
{
    [Fact]
    public async Task RefusesScanWithoutExplicitConsent()
    {
        var scanner = new NetworkScanner(new ConsentStore(false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync());

        Assert.Contains("consentimento", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("001B21-12-34-56", "Intel")]
    [InlineData("00-00-00-00-00-00", "Fabricante desconhecido")]
    public void ResolvesOfflineOui(string mac, string expected)
    {
        Assert.Equal(expected, OuiResolver.Resolve(mac));
    }

    private sealed class ConsentStore(bool hasConsent) : INetworkConsentStore
    {
        public Task<bool> HasConsentAsync(CancellationToken cancellationToken = default) => Task.FromResult(hasConsent);

        public Task GrantConsentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
