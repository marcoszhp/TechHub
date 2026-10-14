using Moq;
using TechHub.App.Services;
using TechHub.App.ViewModels;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Ui.Tests;

public sealed class MainViewModelTests
{
    [Theory]
    [InlineData("diagnostics")]
    [InlineData("drivers")]
    public async Task FailedNavigationCanRetryAndOnlySuccessfulLoadIsCached(string module)
    {
        var diagnostics = new Mock<IDiagnosticService>();
        var drivers = new Mock<IDriverService>();
        diagnostics.SetupSequence(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha simulada"))
            .ReturnsAsync(new DiagnosticSnapshot("Windows", "teste", TimeSpan.Zero, [], [], []));
        drivers.SetupSequence(service => service.GetInventoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriverInventoryResult(false, [], "falha simulada"))
            .ReturnsAsync(new DriverInventoryResult(true, [], "Inventário vazio válido."));
        var diagnosticVm = new DiagnosticsViewModel(diagnostics.Object, Mock.Of<IActionRunner>(), Mock.Of<IActionConfirmationService>());
        var driverVm = new DriversViewModel(drivers.Object, Mock.Of<IActionRunner>(), Mock.Of<IActionConfirmationService>());
        var main = new MainViewModel(null!, null!, null!, diagnosticVm, null!, driverVm,
            null!, null!, null!, null!, null!, null!, null!);

        await main.LoadModuleAsync(module);
        Assert.Contains("Consulta incompleta", main.LoadStatus);
        await main.LoadModuleAsync(module);
        Assert.StartsWith("Pronto", main.LoadStatus);
        await main.LoadModuleAsync(module);
        if (module == "diagnostics") diagnostics.Verify(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        else drivers.Verify(service => service.GetInventoryAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Contains(typeof(MainViewModel).Assembly.GetName().Version!.ToString(3), main.ApplicationVersionLabel);
    }
}
