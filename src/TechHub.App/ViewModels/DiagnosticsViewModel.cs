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

    [ObservableProperty] private string operatingSystem = "Indisponível";
    [ObservableProperty] private string osVersion = "Indisponível";
    [ObservableProperty] private string uptimeText = "Indisponível";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<DiagnosticDiskInfo> Disks { get; } = [];
    public ObservableCollection<DiagnosticEventInfo> RecentCriticalEvents { get; } = [];
    public ObservableCollection<string> Recommendations { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RepairSfcCommand { get; }
    public IAsyncRelayCommand RepairDismCommand { get; }

    public DiagnosticsViewModel(IDiagnosticService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RepairSfcCommand = new AsyncRelayCommand(() => RunRepairAsync(DiagnosticRepairKind.SystemFileChecker));
        RepairDismCommand = new AsyncRelayCommand(() => RunRepairAsync(DiagnosticRepairKind.DeploymentImageServicing));
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var snapshot = await _service.GetSnapshotAsync().ConfigureAwait(true);
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
            StatusMessage = snapshot.Error is null ? "Diagnóstico atualizado." : $"Diagnóstico parcial: {snapshot.Error}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha no diagnóstico: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunRepairAsync(DiagnosticRepairKind kind)
    {
        try
        {
            var request = _service.CreateRepairRequest(kind);
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
            {
                StatusMessage = "Reparo cancelado.";
                return;
            }

            var result = await _actionRunner.ExecuteAsync(
                request,
                new ActionExecutionContext(true, true, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha no reparo: {result.Message}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Reparo não executado: {exception.Message}";
        }
    }
}
