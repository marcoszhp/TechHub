using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class DriversViewModel : ObservableObject
{
    private readonly IDriverService _service;
    private readonly IActionRunner _actionRunner;
    private readonly IActionConfirmationService _confirmation;
    private readonly TimeSpan _refreshTimeout;
    private readonly MaintenanceQuery _query = new();
    private CancellationTokenSource? _refreshCancellation;
    private int _operationActive;

    [ObservableProperty] private string infPath = string.Empty;
    [ObservableProperty] private string backupInfName = string.Empty;
    [ObservableProperty] private string backupDestination = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool lastRefreshSucceeded;

    public ObservableCollection<DriverInfo> Drivers { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand InstallOfflineCommand { get; }
    public IAsyncRelayCommand BackupCommand { get; }
    public IRelayCommand CancelRefreshCommand { get; }

    public DriversViewModel(IDriverService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
        : this(service, actionRunner, confirmation, MaintenanceQuery.DefaultTimeout) { }

    public DriversViewModel(IDriverService service, IActionRunner actionRunner,
        IActionConfirmationService confirmation, TimeSpan refreshTimeout)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        _refreshTimeout = MaintenanceQuery.ValidateTimeout(refreshTimeout);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        InstallOfflineCommand = new AsyncRelayCommand(InstallOfflineAsync, () => !IsBusy);
        BackupCommand = new AsyncRelayCommand(BackupAsync, () => !IsBusy);
        CancelRefreshCommand = new RelayCommand(() => MaintenanceQuery.RequestCancellation(_refreshCancellation), () => IsRefreshing);
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        InstallOfflineCommand.NotifyCanExecuteChanged();
        BackupCommand.NotifyCanExecuteChanged();
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
        try { await RefreshCoreAsync().ConfigureAwait(true); }
        finally { EndOperation(); }
    }

    private async Task RefreshCoreAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        LastRefreshSucceeded = false;
        IsRefreshing = true;
        StatusMessage = "Consultando inventário de drivers...";
        try
        {
            var result = await _query.RunAsync(_service.GetInventoryAsync,
                _refreshTimeout, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.Success || result.Error is not null)
            {
                StatusMessage = $"Falha no inventário: {result.Message} {result.Error}. Os dados anteriores foram mantidos; tente novamente.";
                return;
            }
            Drivers.Clear();
            foreach (var driver in result.Devices) Drivers.Add(driver);
            LastRefreshSucceeded = true;
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Consulta de drivers cancelada. Os dados anteriores foram mantidos.";
        }
        catch (TimeoutException)
        {
            StatusMessage = "O inventário excedeu o tempo limite. Os dados anteriores foram mantidos; tente novamente.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha no inventário: {exception.Message}. Os dados anteriores foram mantidos; tente novamente.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshCancellation = null;
        }
    }

    private async Task InstallOfflineAsync()
    {
        if (!TryBeginOperation()) return;
        try
        {
            if (string.IsNullOrWhiteSpace(InfPath))
            {
                StatusMessage = "Informe o caminho local de um arquivo INF.";
                return;
            }
            var request = await _service.CreateOfflineInstallRequestAsync(InfPath).ConfigureAwait(true);
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
            {
                StatusMessage = "Instalação não autorizada; nenhuma ação executada.";
                return;
            }

            StatusMessage = "Instalando driver. Aguarde a conclusão; esta ação não oferece cancelamento.";
            var result = await _service.ExecuteAsync(request,
                new ActionExecutionContext(true, true, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
            if (result.Success) await RefreshCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Driver não instalado: {exception.Message}";
        }
        finally { EndOperation(); }
    }

    private async Task BackupAsync()
    {
        if (!TryBeginOperation()) return;
        try
        {
            if (string.IsNullOrWhiteSpace(BackupInfName) || string.IsNullOrWhiteSpace(BackupDestination))
            {
                StatusMessage = "Informe o INF publicado e um diretório de destino vazio.";
                return;
            }
            var request = _service.CreateBackupRequest(BackupInfName, BackupDestination);
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
            {
                StatusMessage = "Backup não autorizado; nenhuma ação executada.";
                return;
            }

            StatusMessage = "Executando backup. Aguarde a conclusão; esta ação não oferece cancelamento.";
            var result = await _service.ExecuteAsync(request,
                new ActionExecutionContext(true, false, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Backup não executado: {exception.Message}";
        }
        finally { EndOperation(); }
    }
}
