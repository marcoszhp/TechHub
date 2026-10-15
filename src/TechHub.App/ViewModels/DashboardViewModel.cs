using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;
    private readonly IUpdateService _updateService;
    private readonly IUpdateInstaller _updateInstaller;
    private UpdateInfo? _availableUpdate;
    private CancellationTokenSource? _updateCancellation;
    private int _updateOperation;

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
    [ObservableProperty] private bool isCheckingUpdate;
    [ObservableProperty] private bool isDownloadingUpdate;
    [ObservableProperty] private bool isApplyingUpdate;
    [ObservableProperty] private double updateProgressPercent;
    [ObservableProperty] private string updateProgressText = "";
    [ObservableProperty] private string currentVersion = typeof(DashboardViewModel).Assembly.GetName().Version?.ToString(3) ?? "—";
    [ObservableProperty] private string availableVersion = "—";
    [ObservableProperty] private string releaseNotes = "";

    public ObservableCollection<string> Alerts { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }
    public IRelayCommand CancelUpdateCommand { get; }

    public DashboardViewModel(IDashboardService dashboardService, IUpdateService updateService, IUpdateInstaller updateInstaller)
    {
        _dashboardService = dashboardService;
        _updateService = updateService;
        _updateInstaller = updateInstaller ?? throw new ArgumentNullException(nameof(updateInstaller));
        UpdateStatus = _updateInstaller.LastResultMessage ?? "Atualizações: não verificadas";
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => _updateOperation == 0);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => _updateOperation == 0 && UpdateAvailable);
        CancelUpdateCommand = new RelayCommand(CancelUpdate, () => _updateCancellation is { IsCancellationRequested: false } && !IsApplyingUpdate);
    }

    private async Task CheckUpdatesAsync()
    {
        if (Interlocked.CompareExchange(ref _updateOperation, 1, 0) != 0) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        try
        {
            IsCheckingUpdate = true;
            _availableUpdate = null;
            UpdateAvailable = false;
            AvailableVersion = "—";
            ReleaseNotes = "";
            UpdateProgressPercent = 0;
            UpdateProgressText = "";
            NotifyUpdateCommands();
            UpdateStatus = "Verificando atualizações…";
            var result = await _updateService.CheckAsync(cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            _availableUpdate = result;
            CurrentVersion = result.CurrentVersion;
            AvailableVersion = result.LatestVersion;
            ReleaseNotes = result.ReleaseNotes ?? "";
            UpdateAvailable = CanInstall(result);
            UpdateStatus = result.InstallMode is UpdateInstallMode.Portable or UpdateInstallMode.Unknown
                ? $"{result.Message} Instalação portátil ou desconhecida: atualização manual necessária."
                : result.Message;
        }
        catch (OperationCanceledException) { UpdateStatus = "Verificação cancelada. Você pode tentar novamente."; }
        catch (Exception exception) { UpdateStatus = $"Falha ao verificar atualização: {exception.Message}"; }
        finally
        {
            IsCheckingUpdate = false;
            EndUpdateOperation();
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { } update || !CanInstall(update)) return;
        if (Interlocked.CompareExchange(ref _updateOperation, 1, 0) != 0) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        try
        {
            UpdateProgressPercent = 0;
            UpdateProgressText = "";
            UpdateStatus = "Confirme a atualização e salve seu trabalho antes de reiniciar.";
            NotifyUpdateCommands();
            var confirmed = await _updateInstaller.ConfirmAsync(update).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!confirmed)
            {
                UpdateStatus = "Atualização não autorizada. Nenhum download iniciado.";
                return;
            }
            IsDownloadingUpdate = true;
            UpdateStatus = "Baixando e verificando o instalador…";
            var progress = new DownloadProgress(SynchronizationContext.Current, value =>
            {
                if (_updateCancellation != cancellation || !IsDownloadingUpdate || cancellation.IsCancellationRequested) return;
                UpdateProgressPercent = value.Percent;
                UpdateProgressText = $"{value.BytesReceived:N0} / {value.TotalBytes:N0} bytes ({value.Percent:0}%)";
            });
            var downloaded = await _updateService.DownloadAsync(update, progress, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            IsDownloadingUpdate = false;
            IsApplyingUpdate = true;
            UpdateProgressPercent = 100;
            UpdateStatus = "Aplicando atualização e reiniciando o TechHub… Não feche o aplicativo manualmente.";
            NotifyUpdateCommands();
            await _updateInstaller.ApplyAndRestartAsync(downloaded, CancellationToken.None).ConfigureAwait(true);
            UpdateStatus = "Atualização encaminhada. Aguarde a instalação e o reinício do TechHub.";
        }
        catch (OperationCanceledException) { UpdateStatus = "Atualização cancelada. Você pode tentar novamente."; }
        catch (Exception exception) { UpdateStatus = $"Falha na atualização: {exception.Message}. Você pode tentar novamente."; }
        finally
        {
            IsDownloadingUpdate = false;
            IsApplyingUpdate = false;
            EndUpdateOperation();
        }
    }

    private static bool CanInstall(UpdateInfo update) => update.IsAvailable
        && update.Status == UpdateCheckStatus.Available
        && update.InstallMode is UpdateInstallMode.User or UpdateInstallMode.Machine;

    private void CancelUpdate()
    {
        try
        {
            if (IsApplyingUpdate || _updateCancellation is not { } cancellation) return;
            cancellation.Cancel();
            UpdateStatus = "Cancelando atualização…";
        }
        catch (Exception exception) { UpdateStatus = $"Falha ao solicitar cancelamento: {exception.Message}"; }
        finally { NotifyUpdateCommands(); }
    }

    private void EndUpdateOperation()
    {
        _updateCancellation = null;
        Interlocked.Exchange(ref _updateOperation, 0);
        NotifyUpdateCommands();
    }

    private void NotifyUpdateCommands()
    {
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
        CancelUpdateCommand.NotifyCanExecuteChanged();
    }

    private sealed class DownloadProgress(SynchronizationContext? context, Action<UpdateProgress> report) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value)
        {
            if (context is null || SynchronizationContext.Current == context) report(value);
            else context.Post(_ => report(value), null);
        }
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
