using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechHub.App.Services;
using TechHub.App.ViewModels;
using TechHub.Core.Services;
using TechHub.Infrastructure.Diagnostics;
using TechHub.Infrastructure.Drivers;
using TechHub.PowerShell;

namespace TechHub.Ui.Tests;

// Run explicitly with --filter Category=WindowsIntegration on a consenting Windows machine.
// Only inventory/diagnostic queries run; repair/install/backup dependencies are strict mocks.
public sealed class WindowsReadOnlySmokeTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task ActualDiagnosticsQueryCompletesWithoutMaintenance()
    {
        var runner = new Mock<IActionRunner>(MockBehavior.Strict);
        var confirmation = new Mock<IActionConfirmationService>(MockBehavior.Strict);
        var service = new WindowsDiagnosticService(new PowerShellExecutor(), NullLogger<WindowsDiagnosticService>.Instance);
        var vm = new DiagnosticsViewModel(service, runner.Object, confirmation.Object);
        await vm.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(40));
        Assert.False(vm.IsBusy);
        Assert.True(vm.LastRefreshSucceeded, vm.StatusMessage);
        runner.VerifyNoOtherCalls();
        confirmation.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task ActualDriversInventoryCompletesWithoutMaintenance()
    {
        var runner = new Mock<IActionRunner>(MockBehavior.Strict);
        var confirmation = new Mock<IActionConfirmationService>(MockBehavior.Strict);
        var service = new DriverService(new PowerShellExecutor(), runner.Object);
        var vm = new DriversViewModel(service, runner.Object, confirmation.Object);
        await vm.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(40));
        Assert.False(vm.IsBusy);
        Assert.True(vm.LastRefreshSucceeded, vm.StatusMessage);
        runner.VerifyNoOtherCalls();
        confirmation.VerifyNoOtherCalls();
    }
}
