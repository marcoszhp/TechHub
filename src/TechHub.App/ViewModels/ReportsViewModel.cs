using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _service;

    [ObservableProperty] private string csvPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TechHub-tickets.csv");
    [ObservableProperty] private string jsonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TechHub-tickets.json");
    [ObservableProperty] private string statusMessage = string.Empty;

    public IAsyncRelayCommand ExportCsvCommand { get; }
    public IAsyncRelayCommand ExportJsonCommand { get; }

    public ReportsViewModel(IReportService service)
    {
        _service = service;
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);
        ExportJsonCommand = new AsyncRelayCommand(ExportJsonAsync);
    }

    private async Task ExportCsvAsync()
    {
        try { StatusMessage = $"CSV criado: {await _service.ExportTicketsCsvAsync(CsvPath).ConfigureAwait(true)}"; }
        catch (Exception exception) { StatusMessage = $"Falha no CSV: {exception.Message}"; }
    }

    private async Task ExportJsonAsync()
    {
        try { StatusMessage = $"JSON criado: {await _service.ExportTicketsJsonAsync(JsonPath).ConfigureAwait(true)}"; }
        catch (Exception exception) { StatusMessage = $"Falha no JSON: {exception.Message}"; }
    }
}
