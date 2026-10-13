using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class AssistantViewModel : ObservableObject
{
    private readonly IDiagnosticAssistant _assistant;

    [ObservableProperty] private string symptom = string.Empty;
    [ObservableProperty] private string summary = string.Empty;
    public ObservableCollection<string> Questions { get; } = [];
    public ObservableCollection<string> RecommendedChecks { get; } = [];
    public ObservableCollection<string> SafeActions { get; } = [];
    public IAsyncRelayCommand AskCommand { get; }

    public AssistantViewModel(IDiagnosticAssistant assistant)
    {
        _assistant = assistant;
        AskCommand = new AsyncRelayCommand(AskAsync);
    }

    private async Task AskAsync()
    {
        if (string.IsNullOrWhiteSpace(Symptom)) { Summary = "Descreva o sintoma para começar."; return; }
        try
        {
            var response = await _assistant.AskAsync(Symptom).ConfigureAwait(true);
            Summary = response.Summary;
            Questions.ReplaceWith(response.Questions);
            RecommendedChecks.ReplaceWith(response.RecommendedChecks);
            SafeActions.ReplaceWith(response.SafeActions);
        }
        catch (Exception exception) { Summary = $"Falha: {exception.Message}"; }
    }
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values) collection.Add(value);
    }
}
