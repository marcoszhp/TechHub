using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.App.Services;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class CleaningScopeOption : ObservableObject
{
    public CleaningScope Scope { get; }
    public string Label { get; }
    [ObservableProperty] private bool isSelected;

    public CleaningScopeOption(CleaningScope scope, string label, bool selected = false)
    {
        Scope = scope;
        Label = label;
        IsSelected = selected;
    }
}

public sealed partial class CleaningViewModel : ObservableObject
{
    private readonly ICleaningService _service;
    private readonly IActionRunner _actionRunner;
    private readonly IActionConfirmationService _confirmation;
    private CleaningPreview? _preview;

    [ObservableProperty] private CleaningMode mode = CleaningMode.Safe;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<CleaningScopeOption> Scopes { get; } =
    [
        new(CleaningScope.UserTemp, "Temporários do usuário", true),
        new(CleaningScope.SystemTemp, "Temporários do Windows"),
        new(CleaningScope.WindowsUpdateDownloads, "Downloads do Windows Update"),
        new(CleaningScope.Prefetch, "Prefetch"),
        new(CleaningScope.RecycleBin, "Lixeira"),
        new(CleaningScope.LogsAndDumps, "Logs e dumps"),
        new(CleaningScope.ChromeCache, "Cache Chrome"),
        new(CleaningScope.EdgeCache, "Cache Edge"),
        new(CleaningScope.FirefoxCache, "Cache Firefox")
    ];

    public ObservableCollection<CleaningItem> Items { get; } = [];
    public ObservableCollection<CleaningIssue> Issues { get; } = [];
    public IReadOnlyList<CleaningMode> Modes { get; } = Enum.GetValues<CleaningMode>();
    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IAsyncRelayCommand ExecuteCommand { get; }

    public CleaningViewModel(ICleaningService service, IActionRunner actionRunner, IActionConfirmationService confirmation)
    {
        _service = service;
        _actionRunner = actionRunner;
        _confirmation = confirmation;
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync);
    }

    public async Task AnalyzeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var options = new CleaningOptions(Mode, Scopes.Where(option => option.IsSelected).Select(option => option.Scope).ToArray());
            _preview = await _service.AnalyzeAsync(options).ConfigureAwait(true);
            Items.Clear();
            foreach (var item in _preview.Items) Items.Add(item);
            Issues.Clear();
            foreach (var issue in _preview.Issues) Issues.Add(issue);
            StatusMessage = $"Prévia pronta: {_preview.Items.Count} arquivo(s), {_preview.TotalBytes:N0} bytes. Nada foi apagado.";
        }
        catch (Exception exception)
        {
            _preview = null;
            StatusMessage = $"Falha na análise: {exception.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task ExecuteAsync()
    {
        if (_preview is null)
        {
            StatusMessage = "Gere uma prévia antes de executar a limpeza.";
            return;
        }

        var request = _service.CreateActionRequest(_preview);
        var preview = await _actionRunner.PreviewAsync(request).ConfigureAwait(true);
        if (!await _confirmation.ConfirmAsync(preview).ConfigureAwait(true))
        {
            StatusMessage = "Limpeza cancelada.";
            return;
        }

        var result = await _service.ExecuteAsync(_preview,
            new ActionExecutionContext(true, true, Environment.UserName)).ConfigureAwait(true);
        StatusMessage = result.Success ? result.Message : $"Limpeza incompleta: {result.Message}";
        _preview = null;
        if (result.Success) await AnalyzeAsync().ConfigureAwait(true);
    }
}
