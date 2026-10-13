using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class PrintersViewModel : ObservableObject
{
    private readonly IPrinterService _printerService;
    private readonly IActionRunner _actionRunner;
    private readonly IActionConfirmationService _confirmationService;
    private readonly IPrivilegeService _privilegeService;

    [ObservableProperty] private string ipAddress = string.Empty;
    [ObservableProperty] private string printerName = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public ObservableCollection<PrinterInfo> Printers { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand InstallByIpCommand { get; }

    public PrintersViewModel(
        IPrinterService printerService,
        IActionRunner actionRunner,
        IActionConfirmationService confirmationService,
        IPrivilegeService privilegeService)
    {
        _printerService = printerService;
        _actionRunner = actionRunner;
        _confirmationService = confirmationService;
        _privilegeService = privilegeService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        InstallByIpCommand = new AsyncRelayCommand(InstallByIpAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            Printers.Clear();
            foreach (var printer in await _printerService.GetInventoryAsync().ConfigureAwait(true)) Printers.Add(printer);
            StatusMessage = $"{Printers.Count} impressora(s) encontradas.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha ao listar impressoras: {exception.Message}";
        }
    }

    private async Task InstallByIpAsync()
    {
        if (!_privilegeService.IsAdministrator)
        {
            StatusMessage = "Execute o TechHub como administrador para instalar uma impressora.";
            return;
        }

        var request = _printerService.CreateInstallByIpRequest(IpAddress, PrinterName);
        var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
        if (!await _confirmationService.ConfirmAsync(preview).ConfigureAwait(true))
        {
            StatusMessage = "Instalação cancelada.";
            return;
        }

        var result = await _actionRunner.ExecuteAsync(
            request,
            new ActionExecutionContext(true, false, Environment.UserName)).ConfigureAwait(true);
        StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
        if (result.Success) await RefreshAsync().ConfigureAwait(true);
    }
}
