using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class SoftwareViewModel : ObservableObject
{
    private readonly ISoftwareService _service;
    private readonly IActionRunner _actionRunner;
    private readonly IActionConfirmationService _confirmation;

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string upgradeMessage = string.Empty;
    public ObservableCollection<SoftwareInfo> Software { get; } = [];
    public ObservableCollection<string> UpgradeLines { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CheckUpgradesCommand { get; }
    public IAsyncRelayCommand UpgradeAllCommand { get; }

    public SoftwareViewModel(ISoftwareService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CheckUpgradesCommand = new AsyncRelayCommand(CheckUpgradesAsync);
        UpgradeAllCommand = new AsyncRelayCommand(UpgradeAllAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            Software.Clear();
            foreach (var item in await _service.GetInventoryAsync().ConfigureAwait(true)) Software.Add(item);
            StatusMessage = $"{Software.Count} software(s) encontrado(s) no inventário local.";
        }
        catch (Exception exception) { StatusMessage = $"Falha no inventário: {exception.Message}"; }
    }

    private async Task CheckUpgradesAsync()
    {
        try
        {
            var summary = await _service.GetUpgradeSummaryAsync().ConfigureAwait(true);
            UpgradeLines.Clear();
            foreach (var line in summary.Packages) UpgradeLines.Add(line);
            UpgradeMessage = summary.Message;
        }
        catch (Exception exception) { UpgradeMessage = $"Falha no winget: {exception.Message}"; }
    }

    private async Task UpgradeAllAsync()
    {
        try
        {
            var request = _service.CreateUpgradeAllRequest();
            var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
            if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true)) { StatusMessage = "Atualização cancelada."; return; }
            var result = await _actionRunner.ExecuteAsync(request, new ActionExecutionContext(true, false, Environment.UserName)).ConfigureAwait(true);
            StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
            if (result.Success) await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { StatusMessage = $"Atualização não executada: {exception.Message}"; }
    }
}
