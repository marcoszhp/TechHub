using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;
    private readonly IUpdateService _updateService;
    private UpdateInfo? _availableUpdate;

    [ObservableProperty] private int? healthScore;
    [ObservableProperty] private double? cpuUsagePercent;
    [ObservableProperty] private double? memoryUsagePercent;
    [ObservableProperty] private double? diskUsagePercent;
    [ObservableProperty] private string freeDiskText = "—";
    [ObservableProperty] private string operatingSystem = "—";
    [ObservableProperty] private string privilegeText = "—";
    [ObservableProperty] private string statusMessage = "Carregando métricas…";
    [ObservableProperty] private string updateStatus = "Atualizações: não verificadas";
    [ObservableProperty] private bool updateAvailable;

    public ObservableCollection<string> Alerts { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }

    public DashboardViewModel(IDashboardService dashboardService, IUpdateService updateService)
    {
        _dashboardService = dashboardService;
        _updateService = updateService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => UpdateAvailable);
    }

    private async Task CheckUpdatesAsync()
    {
        UpdateStatus = "Verificando atualizações…";
        var result = await _updateService.CheckAsync().ConfigureAwait(true);
        _availableUpdate = result;
        UpdateAvailable = result.IsAvailable;
        UpdateStatus = result.Message;
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { IsAvailable: true } update) return;
        try
        {
            UpdateStatus = "Baixando o instalador da nova versão…";
            var path = await _updateService.DownloadAndLaunchAsync(update).ConfigureAwait(true);
            UpdateStatus = $"Instalador baixado em {path}. Feche o TechHub para concluir a atualização.";
        }
        catch (Exception exception) { UpdateStatus = $"Falha ao baixar atualização: {exception.Message}"; }
    }

    public async Task RefreshAsync()
    {
        StatusMessage = "Consultando métricas locais…";
        try
        {
            var snapshot = await _dashboardService.GetSnapshotAsync().ConfigureAwait(true);
            HealthScore = snapshot.HealthScore;
            CpuUsagePercent = snapshot.CpuUsagePercent;
            MemoryUsagePercent = snapshot.MemoryUsagePercent;
            DiskUsagePercent = snapshot.DiskUsagePercent;
            FreeDiskText = FormatBytes(snapshot.FreeDiskBytes);
            OperatingSystem = snapshot.OperatingSystem;
            PrivilegeText = snapshot.IsAdministrator ? "Administrador" : "Padrão (ações elevadas bloqueadas)";
            Alerts.Clear();
            foreach (var alert in snapshot.Alerts) Alerts.Add(alert);
            StatusMessage = $"Atualizado em {snapshot.CapturedAt:HH:mm:ss}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Não foi possível obter as métricas: {exception.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "indisponível";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.0} {units[unit]} livres";
    }
}
