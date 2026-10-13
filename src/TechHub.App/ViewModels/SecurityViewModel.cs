using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class SecurityViewModel : ObservableObject
{
    private readonly ISecurityService _service;
    private readonly IActionRunner _actionRunner;
    private readonly IActionConfirmationService _confirmation;

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool defenderEnabled;
    [ObservableProperty] private bool realTimeProtectionEnabled;
    [ObservableProperty] private bool firewallEnabled;
    [ObservableProperty] private bool bitLockerEnabled;
    [ObservableProperty] private bool tpmPresent;
    [ObservableProperty] private bool secureBootEnabled;
    [ObservableProperty] private int localAdministratorCount;

    public ObservableCollection<string> Warnings { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand QuickScanCommand { get; }

    public SecurityViewModel(ISecurityService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        QuickScanCommand = new AsyncRelayCommand(RunQuickScanAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _service.GetSnapshotAsync().ConfigureAwait(true);
            DefenderEnabled = snapshot.DefenderEnabled;
            RealTimeProtectionEnabled = snapshot.RealTimeProtectionEnabled;
            FirewallEnabled = snapshot.FirewallEnabled;
            BitLockerEnabled = snapshot.BitLockerEnabled;
            TpmPresent = snapshot.TpmPresent;
            SecureBootEnabled = snapshot.SecureBootEnabled;
            LocalAdministratorCount = snapshot.LocalAdministratorCount;
            Warnings.Clear();
            foreach (var warning in snapshot.Warnings) Warnings.Add(warning);
            StatusMessage = snapshot.Error is null ? "Estado de segurança atualizado." : $"Consulta parcial: {snapshot.Error}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha na consulta de segurança: {exception.Message}";
        }
    }

    private async Task RunQuickScanAsync()
    {
        var request = _service.CreateActionRequest(SecurityActionKind.QuickDefenderScan);
        var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
        if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
        {
            StatusMessage = "Varredura cancelada.";
            return;
        }

        var result = await _actionRunner.ExecuteAsync(
            request,
            new ActionExecutionContext(true, false, Environment.UserName)).ConfigureAwait(true);
        StatusMessage = result.Success ? result.Message : $"Falha: {result.Message}";
    }
}
