using Moq;
using System.IO;
using TechHub.App.ViewModels;
using TechHub.Core.Services;

namespace TechHub.Ui.Tests;

public sealed class DashboardUpdateTests
{
    private static readonly UpdateInfo Available = new(true, "1.2.1", "1.3.0", "https://example.invalid/release",
        "https://example.invalid/setup.exe", "Nova versão disponível.", UpdateCheckStatus.Available,
        UpdateInstallMode.User, new string('a', 64), 100, "Notas: <script>texto apenas</script>");

    [Fact]
    public Task SuccessConfirmsBeforeDownloadAndAppliesVerifiedResult() => OnUiAsync(async () =>
    {
        var service = new FakeService();
        var installer = new FakeInstaller { LastResultMessage = "Última atualização concluída." };
        var order = new List<string>();
        var package = new DownloadedUpdate(Available, "fake-verified.exe");
        installer.Confirm = _ => { order.Add("confirm"); return Task.FromResult(true); };
        service.Download = (_, progress, _) =>
        {
            order.Add("download");
            progress!.Report(new UpdateProgress(40, 100));
            return Task.FromResult(package);
        };
        installer.Apply = (result, token) =>
        {
            order.Add("apply");
            Assert.Same(package, result);
            Assert.False(token.CanBeCanceled);
            return Task.CompletedTask;
        };
        var vm = Create(service, installer);
        Assert.Equal(installer.LastResultMessage, vm.UpdateStatus);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.Equal("1.2.1", vm.CurrentVersion);
        Assert.Equal("1.3.0", vm.AvailableVersion);
        Assert.Equal(Available.ReleaseNotes, vm.ReleaseNotes);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "confirm", "download", "apply" }, order);
        Assert.Equal(100, vm.UpdateProgressPercent);
        Assert.Contains("reinício", vm.UpdateStatus);
        AssertIdle(vm);
    });

    [Fact]
    public Task RefusalDoesNotDownloadAndAllowsRetry() => OnUiAsync(async () =>
    {
        var service = new FakeService();
        var installer = new FakeInstaller { Confirm = _ => Task.FromResult(false) };
        var vm = Create(service, installer);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, service.DownloadCalls);
        Assert.Equal(0, installer.ApplyCalls);
        Assert.Contains("não autorizada", vm.UpdateStatus);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));
        AssertIdle(vm);
    });

    [Theory]
    [InlineData("check")]
    [InlineData("confirm")]
    [InlineData("download")]
    [InlineData("apply")]
    public Task FailuresAreContainedAndAllowRetry(string stage) => OnUiAsync(async () =>
    {
        var service = new FakeService();
        var installer = new FakeInstaller();
        var vm = Create(service, installer);
        var error = new InvalidOperationException("falha simulada");
        if (stage == "check") service.Check = _ => Task.FromException<UpdateInfo>(error);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        if (stage != "check")
        {
            if (stage == "confirm") installer.Confirm = _ => Task.FromException<bool>(error);
            if (stage == "download") service.Download = (_, _, _) => Task.FromException<DownloadedUpdate>(error);
            if (stage == "apply") installer.Apply = (_, _) => Task.FromException(error);
            await vm.InstallUpdateCommand.ExecuteAsync(null);
            Assert.True(vm.InstallUpdateCommand.CanExecute(null));
        }
        Assert.Contains("falha simulada", vm.UpdateStatus);
        AssertIdle(vm);
        if (stage != "apply") Assert.Equal(0, installer.ApplyCalls);
        service.Check = _ => Task.FromResult(Available);
        installer.Confirm = _ => Task.FromResult(true);
        service.Download = (update, _, _) => Task.FromResult(new DownloadedUpdate(update, "fake.exe"));
        installer.Apply = (_, _) => Task.CompletedTask;
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Contains("reinício", vm.UpdateStatus);
    });

    [Theory]
    [InlineData(UpdateInstallMode.Portable)]
    [InlineData(UpdateInstallMode.Unknown)]
    public Task UnsupportedModesNeverOfferOrStartInstallation(UpdateInstallMode mode) => OnUiAsync(async () =>
    {
        var service = new FakeService { Check = _ => Task.FromResult(Available with { InstallMode = mode }) };
        var installer = new FakeInstaller();
        var vm = Create(service, installer);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.False(vm.UpdateAvailable);
        Assert.False(vm.InstallUpdateCommand.CanExecute(null));
        Assert.Contains("manual", vm.UpdateStatus);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, installer.ConfirmCalls);
        Assert.Equal(0, service.DownloadCalls);
    });

    [Theory]
    [InlineData(UpdateCheckStatus.UpToDate)]
    [InlineData(UpdateCheckStatus.Unsupported)]
    [InlineData(UpdateCheckStatus.Failed)]
    public Task NonAvailableStatusDisablesInstallation(UpdateCheckStatus status) => OnUiAsync(async () =>
    {
        var vm = Create(new FakeService { Check = _ => Task.FromResult(Available with { Status = status }) }, new FakeInstaller());
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.False(vm.UpdateAvailable);
        Assert.False(vm.InstallUpdateCommand.CanExecute(null));
    });

    [Fact]
    public Task CancelCheckDiscardsEvenLateSuccessAndAllowsRetry() => OnUiAsync(async () =>
    {
        var pending = NewCompletion<UpdateInfo>();
        var service = new FakeService { Check = _ => pending.Task };
        var vm = Create(service, new FakeInstaller());
        var operation = vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.True(vm.IsCheckingUpdate);
        Assert.False(vm.CheckUpdatesCommand.CanExecute(null));
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, service.CheckCalls);
        vm.CancelUpdateCommand.Execute(null);
        pending.SetResult(Available);
        await operation;
        Assert.False(vm.UpdateAvailable);
        Assert.Contains("cancelada", vm.UpdateStatus);
        AssertIdle(vm);
        service.Check = _ => Task.FromResult(Available);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.True(vm.UpdateAvailable);
    });

    [Fact]
    public Task CancelDownloadNeverAppliesEvenIfServiceReturnsPackage() => OnUiAsync(async () =>
    {
        var pending = NewCompletion<DownloadedUpdate>();
        CancellationToken downloadToken = default;
        var service = new FakeService { Download = (_, _, token) => { downloadToken = token; return pending.Task; } };
        var installer = new FakeInstaller();
        var vm = Create(service, installer);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        var operation = vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloadingUpdate);
        vm.CancelUpdateCommand.Execute(null);
        Assert.True(downloadToken.IsCancellationRequested);
        pending.SetResult(new DownloadedUpdate(Available, "fake.exe"));
        await operation;
        Assert.Equal(0, installer.ApplyCalls);
        Assert.Contains("cancelada", vm.UpdateStatus);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));
        AssertIdle(vm);
    });

    [Fact]
    public Task CancellationDuringConfirmationPreventsDownload() => OnUiAsync(async () =>
    {
        var pending = NewCompletion<bool>();
        var service = new FakeService();
        var installer = new FakeInstaller { Confirm = _ => pending.Task };
        var vm = Create(service, installer);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        var operation = vm.InstallUpdateCommand.ExecuteAsync(null);
        vm.CancelUpdateCommand.Execute(null);
        pending.SetResult(true);
        await operation;
        Assert.Equal(0, service.DownloadCalls);
        Assert.Equal(0, installer.ApplyCalls);
        AssertIdle(vm);
    });

    [Fact]
    public Task CooperativeDownloadCancellationIsContained() => OnUiAsync(async () =>
    {
        var entered = NewCompletion<bool>();
        var service = new FakeService
        {
            Download = async (update, _, token) =>
            {
                entered.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return new DownloadedUpdate(update, "unreachable.exe");
            }
        };
        var installer = new FakeInstaller();
        var vm = Create(service, installer);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        var operation = vm.InstallUpdateCommand.ExecuteAsync(null);
        await entered.Task;
        vm.CancelUpdateCommand.Execute(null);
        await operation;
        Assert.Equal(0, installer.ApplyCalls);
        Assert.Contains("cancelada", vm.UpdateStatus);
        AssertIdle(vm);
    });

    [Fact]
    public Task FailedRecheckClearsPreviousOffer() => OnUiAsync(async () =>
    {
        var service = new FakeService();
        var vm = Create(service, new FakeInstaller());
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.True(vm.UpdateAvailable);
        service.Check = _ => Task.FromException<UpdateInfo>(new IOException("sem conexão"));
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.False(vm.UpdateAvailable);
        Assert.Equal("—", vm.AvailableVersion);
        Assert.Equal("", vm.ReleaseNotes);
        AssertIdle(vm);
    });

    [Fact]
    public Task ConcurrentCommandsAreRejectedAndApplyingCannotBeCancelled() => OnUiAsync(async () =>
    {
        var confirming = NewCompletion<bool>();
        var applying = NewCompletion<bool>();
        var service = new FakeService();
        var installer = new FakeInstaller { Confirm = _ => confirming.Task, Apply = (_, _) => applying.Task };
        var vm = Create(service, installer);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        var operation = vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.False(vm.CheckUpdatesCommand.CanExecute(null));
        Assert.False(vm.InstallUpdateCommand.CanExecute(null));
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, service.CheckCalls);
        Assert.Equal(1, installer.ConfirmCalls);
        confirming.SetResult(true);
        // Resume the operation on the dispatcher before inspecting the applying state.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        Assert.True(vm.IsApplyingUpdate);
        Assert.False(vm.CancelUpdateCommand.CanExecute(null));
        vm.CancelUpdateCommand.Execute(null);
        Assert.True(vm.IsApplyingUpdate);
        Assert.Contains("Aplicando", vm.UpdateStatus);
        applying.SetResult(true);
        await operation;
        Assert.Equal(1, installer.ApplyCalls);
        AssertIdle(vm);
    });

    [Fact]
    public Task WorkerProgressUpdatesUiAndStaleProgressCannotOverwriteCompletion() => OnUiAsync(async () =>
    {
        var pending = NewCompletion<DownloadedUpdate>();
        IProgress<UpdateProgress>? progress = null;
        var service = new FakeService { Download = (_, reporter, _) => { progress = reporter; return pending.Task; } };
        var vm = Create(service, new FakeInstaller());
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        var operation = vm.InstallUpdateCommand.ExecuteAsync(null);
        await Task.Run(() => progress!.Report(new UpdateProgress(25, 100)));
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        Assert.Equal(25, vm.UpdateProgressPercent);
        Assert.Contains("25%", vm.UpdateProgressText);
        pending.SetResult(new DownloadedUpdate(Available, "fake.exe"));
        await operation;
        progress!.Report(new UpdateProgress(1, 100));
        Assert.Equal(100, vm.UpdateProgressPercent);
        AssertIdle(vm);
    });

    private static DashboardViewModel Create(FakeService service, FakeInstaller installer) =>
        new(Mock.Of<IDashboardService>(), service, installer);

    private static void AssertIdle(DashboardViewModel vm)
    {
        Assert.False(vm.IsCheckingUpdate);
        Assert.False(vm.IsDownloadingUpdate);
        Assert.False(vm.IsApplyingUpdate);
        Assert.True(vm.CheckUpdatesCommand.CanExecute(null));
        Assert.False(vm.CancelUpdateCommand.CanExecute(null));
    }

    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task OnUiAsync(Func<Task> action)
    {
        Task? operation = null;
        await WpfTestHost.RunAsync(() => operation = action());
        await operation!;
    }

    private sealed class FakeService : IUpdateService
    {
        public Func<CancellationToken, Task<UpdateInfo>> Check { get; set; } = _ => Task.FromResult(Available);
        public Func<UpdateInfo, IProgress<UpdateProgress>?, CancellationToken, Task<DownloadedUpdate>> Download { get; set; }
            = (update, _, _) => Task.FromResult(new DownloadedUpdate(update, "fake.exe"));
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return Check(cancellationToken);
        }
        public Task<DownloadedUpdate> DownloadAsync(UpdateInfo update, IProgress<UpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            return Download(update, progress, cancellationToken);
        }
    }

    private sealed class FakeInstaller : IUpdateInstaller
    {
        public string? LastResultMessage { get; init; }
        public Func<UpdateInfo, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(true);
        public Func<DownloadedUpdate, CancellationToken, Task> Apply { get; set; } = (_, _) => Task.CompletedTask;
        public int ConfirmCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public Task<bool> ConfirmAsync(UpdateInfo update)
        {
            ConfirmCalls++;
            return Confirm(update);
        }
        public Task ApplyAndRestartAsync(DownloadedUpdate update, CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Apply(update, cancellationToken);
        }
    }
}
