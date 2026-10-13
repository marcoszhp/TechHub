using TechHub.Core.Services;
using TechHub.Infrastructure.Automation;

namespace TechHub.Tests;

public sealed class Phase4Tests
{
    [Fact]
    public void SubnetCalculator_HandlesSlash24()
    {
        var result = new SubnetCalculator().Calculate("192.168.10.0/24");

        Assert.Equal("192.168.10.0", result.Network);
        Assert.Equal("192.168.10.255", result.Broadcast);
        Assert.Equal("192.168.10.1", result.FirstHost);
        Assert.Equal("192.168.10.254", result.LastHost);
        Assert.Equal(254, result.HostCount);
    }

    [Fact]
    public async Task OfflineAssistant_ReturnsConnectivityGuidance()
    {
        var response = await new OfflineDiagnosticAssistant().AskAsync("Estou sem internet");

        Assert.Equal("conectividade", response.Symptom);
        Assert.NotEmpty(response.RecommendedChecks);
        Assert.Contains(response.SafeActions, item => item.Contains("roteador", StringComparison.OrdinalIgnoreCase));
    }
}
