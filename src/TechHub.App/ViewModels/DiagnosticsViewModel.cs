using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticService _service;
    private readonly IActionRunner _actionRunner;
    private readonly IActionConfirmationService _confirmation;
    private readonly TimeSpan _refreshTimeout;
    private readonly MaintenanceQuery _query = new();
    private CancellationTokenSource? _refreshCancellation;
    private int _operationActive;

    [ObservableProperty] private string operatingSystem = "Indisponível";
    [ObservableProperty] private string osVersion = "Indisponível";
    [ObservableProperty] private string uptimeText = "Indisponível";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool lastRefreshSucceeded;

    public ObservableCollection<DiagnosticDiskInfo> Disks { get; } = [];
    public ObservableCollection<DiagnosticEventInfo> RecentCriticalEvents { get; } = [];
    public ObservableCollection<string> Recommendations { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RepairSfcCommand { get; }
    public IAsyncRelayCommand RepairDismCommand { get; }
    public IRelayCommand CancelRefreshCommand { get; }

    public DiagnosticsViewModel(IDiagnosticService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
        : this(service, actionRunner, confirmation, MaintenanceQuery.DefaultTimeout) { }

    public DiagnosticsViewModel(IDiagnosticService service, IActionRunner actionRunner,
        IActionConfirmationService confirmation, TimeSpan refreshTimeout)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        _refreshTimeout = MaintenanceQuery.ValidateTimeout(refreshTimeout);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RepairSfcCommand = new AsyncRelayCommand(() => RunRepairAsync(DiagnosticRepairKind.SystemFileChecker), () => !IsBusy);
        RepairDismCommand = new AsyncRelayCommand(() => RunRepairAsync(DiagnosticRepairKind.DeploymentImageServicing), () => !IsBusy);
        CancelRefreshCommand = new RelayCommand(() => MaintenanceQuery.RequestCancellation(_refreshCancellation), () => IsRefreshing);
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RepairSfcCommand.NotifyCanExecuteChanged();
        RepairDismCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRefreshingChanged(bool value) => CancelRefreshCommand.NotifyCanExecuteChanged();

    private bool TryBeginOperation()
    {
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0) return false;
        IsBusy = true;
        return true;
    }

    private void EndOperation()
    {
        Interlocked.Exchange(ref _operationActive, 0);
        IsBusy = false;
    }

    public async Task RefreshAsync()
    {
        if (!TryBeginOperation()) return;
        using var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        LastRefreshSucceeded = false;
        IsRefreshing = true;
        StatusMessage = "Consultando diagnóstico...";
        try
        {
            var snapshot = await _query.RunAsync(_service.GetSnapshotAsync,
                _refreshTimeout, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (snapshot.Error is not null)
            {
                StatusMessage = $"Diagnóstico parcial: {snapshot.Error}. Os dados anteriores foram mantidos; tente novamente.";
                return;
            }
            OperatingSystem = snapshot.OperatingSystem;
            OsVersion = snapshot.OsVersion;
            UptimeText = snapshot.Uptime.TotalHours < 24
                ? $"{snapshot.Uptime.TotalHours:0.0} hora(s)"
                : $"{snapshot.Uptime.TotalDays:0.0} dia(s)";
            Disks.Clear();
            foreach (var disk in snapshot.Disks) Disks.Add(disk);
            RecentCriticalEvents.Clear();
            foreach (var item in snapshot.RecentCriticalEvents) RecentCriticalEvents.Add(item);
            Recommendations.Clear();
            foreach (var item in snapshot.Recommendations) Recommendations.Add(item);
            LastRefreshSucceeded = true;
            StatusMessage = "Diagnóstico atualizado.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Consulta de diagnóstico cancelada. Os dados anteriores foram mantidos.";
        }
        catch (TimeoutException)
        {
            StatusMessage = "O diagnóstico excedeu o tempo limite. Os dados anteriores foram mantidos; tente novamente.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha no diagnóstico: {exception.Message}. Os dados anteriores foram mantidos; tente novamente.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshCancellation = null;
            EndOperation();
        }
    }

    private async Task RunRepairAsync(DiagnosticRepairKind kind)
    {
        if (!TryBeginOperation()) return;
        try
        {
            var request = _service.CreateRepairRequest(kind);
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
            {
                StatusMessage = "Reparo não autorizado; nenhuma ação executada.";
                return;
            }

            StatusMessage = "Executando reparo. Aguarde a conclusão; esta ação não oferece cancelamento.";
            var result = await _actionRunner.ExecuteAsync(
                request,
                new ActionExecutionContext(true, true, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha no reparo: {result.Message}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Reparo não executado: {exception.Message}";
        }
        finally { EndOperation(); }
    }
}
