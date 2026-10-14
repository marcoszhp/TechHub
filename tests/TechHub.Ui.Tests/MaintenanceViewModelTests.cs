using CommunityToolkit.Mvvm.Input;
using Moq;
using TechHub.App.Services;
using TechHub.App.ViewModels;
using TechHub.Core.Models;
using TechHub.Core.Services;
using Xunit;

namespace TechHub.Ui.Tests;

public sealed class MaintenanceViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedQueryPreservesDataAndAllowsRetry(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        await harness.Refresh();
        Assert.True(harness.Succeeded());
        harness.Query = _ => Task.FromException<QueryResult>(new InvalidOperationException("falha simulada"));

        await harness.Refresh();

        Assert.False(harness.Succeeded());
        Assert.Equal("anterior", harness.Name());
        Assert.Contains("falha simulada", harness.Status());
        AssertIdle(harness);
        harness.Query = _ => Task.FromResult(new QueryResult("novo"));
        await harness.RefreshCommand.ExecuteAsync(null);
        Assert.True(harness.Succeeded());
        Assert.Equal("novo", harness.Name());
        AssertIdle(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialQueryDoesNotReplacePreviousDataOrReportSuccess(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        await harness.Refresh();
        harness.Query = _ => Task.FromResult(new QueryResult("parcial", "coleta incompleta"));

        await harness.Refresh();

        Assert.False(harness.Succeeded());
        Assert.Equal("anterior", harness.Name());
        Assert.Contains("coleta incompleta", harness.Status());
        AssertIdle(harness);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancelUncooperativeQueryAllowsRetryAndIgnoresLateCompletion(bool drivers, bool faultLate)
    {
        var harness = new QueryHarness(drivers);
        await harness.Refresh();
        var pending = NewCompletion<QueryResult>();
        harness.Query = _ => pending.Task; // Deliberately ignores cancellation.
        var refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(harness.Busy());
        Assert.True(harness.Refreshing());
        Assert.False(harness.Succeeded());
        Assert.True(harness.LastToken.CanBeCanceled);
        Assert.True(harness.CancelCommand.CanExecute(null));

        harness.CancelCommand.Execute(null);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(harness.LastToken.IsCancellationRequested);
        Assert.False(harness.Succeeded());
        Assert.Equal("anterior", harness.Name());
        Assert.Contains("cancelada", harness.Status());
        AssertIdle(harness);
        harness.Query = _ => Task.FromResult(new QueryResult("repetida"));
        await harness.Refresh();
        var status = harness.Status();
        if (faultLate) pending.SetException(new InvalidOperationException("erro tardio"));
        else pending.SetResult(new QueryResult("tardia"));
        // Wait for the fake backend itself, without requiring a real maintenance action.
        await harness.LastAbandonedQuery!.ContinueWith(_ => { }, TaskScheduler.Default);
        await Task.Yield();
        Assert.Equal("repetida", harness.Name());
        Assert.Equal(status, harness.Status());
        Assert.True(harness.Succeeded());
        AssertIdle(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutCancelsBackendTokenPreservesDataAndAllowsRetry(bool drivers)
    {
        var harness = new QueryHarness(drivers, TimeSpan.FromMilliseconds(500));
        await harness.Refresh();
        var pending = NewCompletion<QueryResult>();
        harness.Query = _ => pending.Task;

        var refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(harness.LastToken.IsCancellationRequested);
        Assert.False(harness.Succeeded());
        Assert.Contains("tempo limite", harness.Status());
        Assert.Equal("anterior", harness.Name());
        AssertIdle(harness);
        harness.Query = _ => Task.FromResult(new QueryResult("repetida"));
        await harness.Refresh();
        pending.SetResult(new QueryResult("tardia"));
        await harness.LastAbandonedQuery!.ContinueWith(_ => { }, TaskScheduler.Default);
        await Task.Yield();
        Assert.Equal("repetida", harness.Name());
        Assert.True(harness.Succeeded());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CooperativeCancellationIsReportedWithoutFailureOrTimeout(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        harness.Query = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new QueryResult("não alcançado");
        };
        var refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.CancelCommand.Execute(null);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(harness.Succeeded());
        Assert.Contains("cancelada", harness.Status());
        Assert.DoesNotContain("tempo limite", harness.Status());
        AssertIdle(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshBlocksOtherCommandsEvenWhenExecuteAsyncBypassesCanExecute(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        var pending = NewCompletion<QueryResult>();
        harness.Query = _ => pending.Task;
        var refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(harness.RefreshCommand.CanExecute(null));
        await harness.Refresh();
        await harness.RefreshCommand.ExecuteAsync(null);
        foreach (var command in harness.Actions)
        {
            Assert.False(command.CanExecute(null));
            await command.ExecuteAsync(null);
        }
        Assert.Equal(1, harness.QueryCalls);
        harness.Runner.VerifyNoOtherCalls();
        harness.Confirmation.VerifyNoOtherCalls();
        pending.SetResult(new QueryResult("concluída"));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(harness.Succeeded());
        AssertIdle(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoAbandonedQueriesBoundRetriesAndRecoverAfterBackendEnds(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        await harness.Refresh();
        var first = NewCompletion<QueryResult>();
        var second = NewCompletion<QueryResult>();
        harness.Query = _ => first.Task;
        var refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstBackend = harness.LastAbandonedQuery!;
        harness.CancelCommand.Execute(null);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Query = _ => second.Task;
        refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondBackend = harness.LastAbandonedQuery!;
        harness.CancelCommand.Execute(null);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        var calls = harness.QueryCalls;
        harness.Query = _ => Task.FromResult(new QueryResult("recuperada"));

        await harness.Refresh();
        await harness.Refresh();

        Assert.Equal(calls, harness.QueryCalls);
        Assert.False(harness.Succeeded());
        Assert.Equal("anterior", harness.Name());
        Assert.Contains("Consultas anteriores ainda estão encerrando; aguarde", harness.Status());
        AssertIdle(harness);
        first.SetResult(new QueryResult("tardia 1"));
        await firstBackend.WaitAsync(TimeSpan.FromSeconds(5));
        // Backend completion and the observer that releases its slot are separate
        // continuations. Retry until the released slot is visible, with a deadline.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!harness.Succeeded())
        {
            await harness.Refresh();
            if (!harness.Succeeded()) await Task.Delay(10, deadline.Token);
        }
        Assert.Equal(calls + 1, harness.QueryCalls);
        Assert.Equal("recuperada", harness.Name());
        var status = harness.Status();
        second.SetException(new InvalidOperationException("falha tardia simulada"));
        await secondBackend.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("recuperada", harness.Name());
        Assert.Equal(status, harness.Status());
        Assert.True(harness.Succeeded());
        AssertIdle(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousBackendPrefixCannotBlockRefreshCaller(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        using var release = new ManualResetEventSlim();
        var backendEntered = NewCompletion<bool>();
        var callerReturned = NewCompletion<Task>();
        harness.Query = _ =>
        {
            backendEntered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("fake watchdog");
            return Task.FromResult(new QueryResult("concluída"));
        };
        var caller = new Thread(() => callerReturned.TrySetResult(harness.Refresh())) { IsBackground = true };
        caller.Start();
        try
        {
            await backendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var refresh = await callerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(refresh.IsCompleted);
            Assert.True(harness.Refreshing());
            release.Set();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(harness.Succeeded());
            AssertIdle(harness);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingCancellationCallbackCannotBlockCommandOrRefreshCompletion(bool drivers)
    {
        var harness = new QueryHarness(drivers);
        using var release = new ManualResetEventSlim();
        var callbackEntered = NewCompletion<bool>();
        var cancelReturned = NewCompletion<bool>();
        var pending = NewCompletion<QueryResult>();
        CancellationTokenRegistration registration = default;
        harness.Query = token =>
        {
            registration = token.Register(() =>
            {
                callbackEntered.TrySetResult(true);
                release.Wait(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("fake callback failure");
            });
            return pending.Task;
        };
        var refresh = harness.Refresh();
        await harness.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var caller = new Thread(() =>
        {
            harness.CancelCommand.Execute(null);
            cancelReturned.TrySetResult(true);
        }) { IsBackground = true };
        caller.Start();
        try
        {
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancelReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await refresh.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(harness.Succeeded());
            Assert.Contains("cancelada", harness.Status());
            AssertIdle(harness);
        }
        finally
        {
            release.Set();
            pending.TrySetResult(new QueryResult("tardia"));
            await registration.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairLocksRefreshAndOtherRepairAndDoesNotOfferCancellation(bool dism)
    {
        var service = new Mock<IDiagnosticService>(MockBehavior.Strict);
        var runner = new Mock<IActionRunner>(MockBehavior.Strict);
        var confirmation = new Mock<IActionConfirmationService>(MockBehavior.Strict);
        var request = FakeRequest();
        var preview = Preview(request);
        var pending = NewCompletion<ActionResult>();
        service.Setup(x => x.CreateRepairRequest(It.IsAny<DiagnosticRepairKind>())).Returns(request);
        runner.Setup(x => x.PreviewAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(preview);
        confirmation.Setup(x => x.ConfirmAsync(preview)).ReturnsAsync(true);
        runner.Setup(x => x.ExecuteAsync(request, It.IsAny<ActionExecutionContext>(),
            It.IsAny<IProgress<ActionProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns((ActionRequest _, ActionExecutionContext _, IProgress<ActionProgress>? _, CancellationToken token) =>
            {
                Assert.False(token.CanBeCanceled);
                return pending.Task;
            });
        var vm = new DiagnosticsViewModel(service.Object, runner.Object, confirmation.Object);
        var active = dism ? vm.RepairDismCommand : vm.RepairSfcCommand;
        var other = dism ? vm.RepairSfcCommand : vm.RepairDismCommand;
        var repair = active.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.False(vm.IsRefreshing);
        Assert.False(vm.CancelRefreshCommand.CanExecute(null));
        vm.CancelRefreshCommand.Execute(null);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(other.CanExecute(null));
        await vm.RefreshAsync();
        await other.ExecuteAsync(null);
        service.Verify(x => x.CreateRepairRequest(It.IsAny<DiagnosticRepairKind>()), Times.Once);
        service.Verify(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()), Times.Never);
        pending.SetResult(new ActionResult(true, "reparo simulado", TimeSpan.Zero));
        await repair.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsBusy);
        Assert.True(other.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DriverActionLocksOtherCommandsAndInstallRefreshesInsideSameLock(bool install)
    {
        var service = new Mock<IDriverService>(MockBehavior.Strict);
        var runner = new Mock<IActionRunner>(MockBehavior.Strict);
        var confirmation = new Mock<IActionConfirmationService>(MockBehavior.Strict);
        var request = FakeRequest();
        var preview = Preview(request);
        var pending = NewCompletion<ActionResult>();
        var inventory = NewCompletion<DriverInventoryResult>();
        service.Setup(x => x.CreateBackupRequest("oem1.inf", "fake-destination")).Returns(request);
        service.Setup(x => x.CreateOfflineInstallRequestAsync("fake.inf", It.IsAny<CancellationToken>())).ReturnsAsync(request);
        service.Setup(x => x.GetInventoryAsync(It.IsAny<CancellationToken>())).Returns(inventory.Task);
        runner.Setup(x => x.PreviewAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(preview);
        confirmation.Setup(x => x.ConfirmAsync(preview)).ReturnsAsync(true);
        service.Setup(x => x.ExecuteAsync(request, It.IsAny<ActionExecutionContext>(),
            It.IsAny<IProgress<ActionProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns((ActionRequest _, ActionExecutionContext _, IProgress<ActionProgress>? _, CancellationToken token) =>
            {
                Assert.False(token.CanBeCanceled);
                return pending.Task;
            });
        var vm = new DriversViewModel(service.Object, runner.Object, confirmation.Object)
        {
            InfPath = "fake.inf", BackupInfName = "oem1.inf", BackupDestination = "fake-destination"
        };
        var active = install ? vm.InstallOfflineCommand : vm.BackupCommand;
        var other = install ? vm.BackupCommand : vm.InstallOfflineCommand;
        var queryStarted = NewCompletion<bool>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsRefreshing) && vm.IsRefreshing) queryStarted.TrySetResult(true);
        };
        var action = active.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsRefreshing);
        Assert.False(vm.CancelRefreshCommand.CanExecute(null));
        vm.CancelRefreshCommand.Execute(null);
        await vm.RefreshAsync();
        Assert.False(other.CanExecute(null));
        await other.ExecuteAsync(null);
        service.Verify(x => x.ExecuteAsync(request, It.IsAny<ActionExecutionContext>(),
            It.IsAny<IProgress<ActionProgress>?>(), It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetInventoryAsync(It.IsAny<CancellationToken>()), Times.Never);
        pending.SetResult(new ActionResult(true, "ação simulada", TimeSpan.Zero));
        if (install)
        {
            await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.IsBusy);
            Assert.True(vm.IsRefreshing);
            Assert.True(vm.CancelRefreshCommand.CanExecute(null));
            await vm.RefreshAsync();
            await other.ExecuteAsync(null);
            inventory.SetResult(new DriverInventoryResult(true, [], "inventário simulado"));
        }
        await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsRefreshing);
        Assert.True(other.CanExecute(null));
        service.Verify(x => x.GetInventoryAsync(It.IsAny<CancellationToken>()), install ? Times.Once() : Times.Never());
        Assert.Equal(install, vm.LastRefreshSucceeded);
    }

    private static void AssertIdle(QueryHarness harness)
    {
        Assert.False(harness.Busy());
        Assert.False(harness.Refreshing());
        Assert.False(harness.CancelCommand.CanExecute(null));
        Assert.True(harness.RefreshCommand.CanExecute(null));
        foreach (var command in harness.Actions) Assert.True(command.CanExecute(null));
    }

    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ActionRequest FakeRequest() => new("fake", "fake", "fake", ActionRisk.Elevated, true, [],
        (_, _) => throw new InvalidOperationException("Fake callbacks must never execute."));
    private static ActionPreview Preview(ActionRequest request) => new(request.Id, request.Title, request.Description,
        request.Risk, request.RequiresAdministrator, request.AffectedResources);
    private sealed record QueryResult(string Name, string? Error = null);

    private sealed class QueryHarness
    {
        public Mock<IActionRunner> Runner { get; } = new(MockBehavior.Strict);
        public Mock<IActionConfirmationService> Confirmation { get; } = new(MockBehavior.Strict);
        private Func<CancellationToken, Task<QueryResult>> _query = _ => Task.FromResult(new QueryResult("anterior"));
        public TaskCompletionSource<bool> QueryStarted { get; private set; } = NewCompletion<bool>();
        public Func<CancellationToken, Task<QueryResult>> Query
        {
            get => _query;
            set { _query = value; QueryStarted = NewCompletion<bool>(); }
        }
        public CancellationToken LastToken { get; private set; }
        public Task? LastAbandonedQuery { get; private set; }
        public int QueryCalls { get; private set; }
        public Func<Task> Refresh { get; }
        public Func<bool> Busy { get; }
        public Func<bool> Refreshing { get; }
        public Func<bool> Succeeded { get; }
        public Func<string> Status { get; }
        public Func<string> Name { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IAsyncRelayCommand[] Actions { get; }

        public QueryHarness(bool drivers, TimeSpan? timeout = null)
        {
            if (drivers)
            {
                var service = new Mock<IDriverService>(MockBehavior.Strict);
                service.Setup(x => x.GetInventoryAsync(It.IsAny<CancellationToken>()))
                    .Returns((CancellationToken token) => Track(InventoryAsync(token), token));
                var vm = new DriversViewModel(service.Object, Runner.Object, Confirmation.Object, timeout ?? TimeSpan.FromSeconds(30))
                {
                    InfPath = "fake.inf", BackupInfName = "oem1.inf", BackupDestination = "fake"
                };
                Refresh = vm.RefreshAsync;
                Busy = () => vm.IsBusy;
                Refreshing = () => vm.IsRefreshing;
                Succeeded = () => vm.LastRefreshSucceeded;
                Status = () => vm.StatusMessage;
                Name = () => vm.Drivers.Single().DeviceName;
                RefreshCommand = vm.RefreshCommand;
                CancelCommand = vm.CancelRefreshCommand;
                Actions = [vm.BackupCommand, vm.InstallOfflineCommand];
            }
            else
            {
                var service = new Mock<IDiagnosticService>(MockBehavior.Strict);
                service.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                    .Returns((CancellationToken token) => Track(SnapshotAsync(token), token));
                var vm = new DiagnosticsViewModel(service.Object, Runner.Object, Confirmation.Object, timeout ?? TimeSpan.FromSeconds(30));
                Refresh = vm.RefreshAsync;
                Busy = () => vm.IsBusy;
                Refreshing = () => vm.IsRefreshing;
                Succeeded = () => vm.LastRefreshSucceeded;
                Status = () => vm.StatusMessage;
                Name = () => vm.OperatingSystem;
                RefreshCommand = vm.RefreshCommand;
                CancelCommand = vm.CancelRefreshCommand;
                Actions = [vm.RepairSfcCommand, vm.RepairDismCommand];
            }
        }

        private Task<T> Track<T>(Task<T> task, CancellationToken token)
        {
            QueryCalls++;
            LastToken = token;
            if (!task.IsCompleted) LastAbandonedQuery = task;
            QueryStarted.TrySetResult(true);
            return task;
        }

        private async Task<DiagnosticSnapshot> SnapshotAsync(CancellationToken token)
        {
            var result = await Query(token);
            return new DiagnosticSnapshot(result.Name, "versão", TimeSpan.FromHours(1),
                [new DiagnosticDiskInfo(result.Name, "OK", 1, null)], [], [result.Name], result.Error);
        }

        private async Task<DriverInventoryResult> InventoryAsync(CancellationToken token)
        {
            var result = await Query(token);
            return new DriverInventoryResult(result.Error is null,
                [new DriverInfo("fake-id", result.Name, null, null, null, null, null, null, null, null, null, null)],
                "inventário simulado", result.Error);
        }
    }
}
