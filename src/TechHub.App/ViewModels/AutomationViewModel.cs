using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class AutomationViewModel : ObservableObject
{
    private readonly IAutomationService _service;
    private readonly IActionConfirmationService _confirmation;

    [ObservableProperty] private AutomationPlaybook? selectedPlaybook;
    [ObservableProperty] private string statusMessage = string.Empty;
    public ObservableCollection<AutomationPlaybook> Playbooks { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RunCommand { get; }

    public AutomationViewModel(IAutomationService service, IActionConfirmationService confirmation)
    {
        _service = service;
        _confirmation = confirmation;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RunCommand = new AsyncRelayCommand(RunAsync);
    }

    public async Task RefreshAsync()
    {
        try { Playbooks.Clear(); foreach (var item in await _service.GetPlaybooksAsync().ConfigureAwait(true)) Playbooks.Add(item); StatusMessage = $"{Playbooks.Count} playbook(s) carregado(s)."; }
        catch (Exception exception) { StatusMessage = $"Falha: {exception.Message}"; }
    }

    private async Task RunAsync()
    {
        if (SelectedPlaybook is null) { StatusMessage = "Selecione um playbook."; return; }
        var preview = new ActionPreview($"automation.{SelectedPlaybook.Id}", SelectedPlaybook.Name, SelectedPlaybook.Description, ActionRisk.Safe, false, SelectedPlaybook.Steps.Select(step => step.Name).ToArray());
        if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true)) { StatusMessage = "Playbook cancelado."; return; }
        var result = await _service.RunAsync(SelectedPlaybook.Id, true, Environment.UserName).ConfigureAwait(true);
        StatusMessage = result.Message;
    }
}
