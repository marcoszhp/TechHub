using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Infrastructure.Actions;
using TechHub.Infrastructure.Drivers;

namespace TechHub.Tests;

public sealed class DriverServiceTests
{
    // Doubles explícitos apenas nas fronteiras dos testes de bloqueio.
    // Não simulam inventário, assinatura válida ou execução de PnPUtil.
    private readonly Mock<IPowerShellExecutor> shell = new(MockBehavior.Strict);
    private readonly Mock<IAuditStore> audit = new(MockBehavior.Strict);

    private DriverService CreateService(bool administrator = false)
    {
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var privilege = new Mock<IPrivilegeService>();
        privilege.SetupGet(x => x.IsAdministrator).Returns(administrator);
        var runner = new ActionRunner(audit.Object, Mock.Of<IRestorePointService>(), privilege.Object, NullLogger<ActionRunner>.Instance);
        return new DriverService(shell.Object, runner);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("driver.inf")]
    [InlineData("oem1.inf /delete-driver")]
    [InlineData("../oem1.inf")]
    public void BackupRejectsAmbiguousDriverScope(string inf) =>
        Assert.Throws<ArgumentException>(() => CreateService().CreateBackupRequest(inf, @"C:\TechHubDriverTestBackup"));

    [Theory]
    [InlineData("https://www.microsoft.com/driver.inf")]
    [InlineData("https://example.org/driver.inf")]
    [InlineData(@"\\server\share\driver.inf")]
    [InlineData(@"C:\driver.inf:stream")]
    [InlineData(@"C:\*.inf")]
    [InlineData("driver.inf")]
    public async Task VerificationRejectsNonLocalOrAmbiguousPaths(string path)
    {
        var result = await CreateService().VerifyOfflineDriverAsync(path);
        Assert.False(result.Success);
        Assert.False(result.IsTrusted);
        Assert.Empty(result.Files);
        Assert.NotNull(result.Error);
        shell.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BackupCallbackCannotBypassRunner()
    {
        var request = CreateService().CreateBackupRequest("oem42.inf", @"C:\TechHubDriverTestBackup");
        Assert.Equal(ActionRisk.Elevated, request.Risk);
        Assert.True(request.RequiresAdministrator);
        Assert.Contains("Driver publicado: oem42.inf", request.AffectedResources);
        Assert.Contains(@"Diretório de backup: C:\TechHubDriverTestBackup", request.AffectedResources);
        var result = await request.ExecuteAsync(CancellationToken.None, null);
        Assert.False(result.Success);
        Assert.Contains("ActionRunner", result.Message);
        shell.VerifyNoOtherCalls();
        audit.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, true, "administrador")]
    [InlineData(true, false, "confirmação")]
    public async Task RunnerEnforcesPrivilegeAndConsentAndAudits(bool administrator, bool confirmed, string reason)
    {
        var service = CreateService(administrator);
        var request = service.CreateBackupRequest("oem42.inf", @"C:\TechHubDriverTestBackup");
        var result = await service.ExecuteAsync(request, new ActionExecutionContext(confirmed, false, "teste"));
        Assert.False(result.Success);
        Assert.Contains(reason, result.Message);
        audit.Verify(x => x.AppendAsync(It.Is<AuditEntry>(entry => !entry.Success && entry.ActionId == "driver.backup"),
            It.IsAny<CancellationToken>()), Times.Once);
        shell.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AlteredRequestCannotWeakenRunnerRequirements()
    {
        var service = CreateService();
        var request = service.CreateBackupRequest("oem42.inf", @"C:\TechHubDriverTestBackup");
        var altered = request with { Risk = ActionRisk.ReadOnly, RequiresAdministrator = false };
        var result = await service.ExecuteAsync(altered, new ActionExecutionContext(true, false, "teste"));
        Assert.False(result.Success);
        Assert.Contains("não pertence", result.Message);
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task QueriesHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetInventoryAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.VerifyOfflineDriverAsync(@"C:\driver.inf", cancellation.Token));
        shell.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(28u, "OK", true)]
    [InlineData(0u, "Error", true)]
    [InlineData(0u, "Degraded", true)]
    [InlineData(0u, "OK", false)]
    public void DetectsDeviceProblems(uint code, string status, bool expected)
    {
        var device = new DriverInfo("id", "dispositivo", null, null, null, null, null, null, null, null, code, status);
        Assert.Equal(expected, device.HasProblem);
    }
}
