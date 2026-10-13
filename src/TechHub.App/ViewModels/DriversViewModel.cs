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

    [ObservableProperty] private string infPath = string.Empty;
    [ObservableProperty] private string backupInfName = string.Empty;
    [ObservableProperty] private string backupDestination = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<DriverInfo> Drivers { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand InstallOfflineCommand { get; }
    public IAsyncRelayCommand BackupCommand { get; }

    public DriversViewModel(IDriverService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        InstallOfflineCommand = new AsyncRelayCommand(InstallOfflineAsync);
        BackupCommand = new AsyncRelayCommand(BackupAsync);
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _service.GetInventoryAsync().ConfigureAwait(true);
            Drivers.Clear();
            foreach (var driver in result.Devices) Drivers.Add(driver);
            StatusMessage = result.Success ? result.Message : $"Falha: {result.Message} {result.Error}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha no inventário: {exception.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task InstallOfflineAsync()
    {
        if (string.IsNullOrWhiteSpace(InfPath))
        {
            StatusMessage = "Informe o caminho local de um arquivo INF.";
            return;
        }

        try
        {
            var request = await _service.CreateOfflineInstallRequestAsync(InfPath).ConfigureAwait(true);
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
            {
                StatusMessage = "Instalação cancelada.";
                return;
            }

            var result = await _service.ExecuteAsync(request,
                new ActionExecutionContext(true, true, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
            if (result.Success) await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Driver não instalado: {exception.Message}";
        }
    }

    private async Task BackupAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupInfName) || string.IsNullOrWhiteSpace(BackupDestination))
        {
            StatusMessage = "Informe o INF publicado e um diretório de destino vazio.";
            return;
        }

        try
        {
            var request = _service.CreateBackupRequest(BackupInfName, BackupDestination);
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
            {
                StatusMessage = "Backup cancelado.";
                return;
            }

            var result = await _service.ExecuteAsync(request,
                new ActionExecutionContext(true, false, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Backup não executado: {exception.Message}";
        }
    }
}
