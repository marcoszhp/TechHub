using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly INetworkConsentStore _consentStore;
    private readonly INetworkScanner _scanner;

    [ObservableProperty] private bool hasConsent;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string statusMessage = "A varredura exige consentimento explícito.";
    [ObservableProperty] private double progress;

    public ObservableCollection<NetworkDeviceInfo> Devices { get; } = [];
    public IAsyncRelayCommand GrantConsentCommand { get; }
    public IAsyncRelayCommand ScanCommand { get; }

    public NetworkViewModel(INetworkConsentStore consentStore, INetworkScanner scanner)
    {
        _consentStore = consentStore;
        _scanner = scanner;
        GrantConsentCommand = new AsyncRelayCommand(GrantConsentAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync);
    }

    public async Task InitializeAsync()
    {
        HasConsent = await _consentStore.HasConsentAsync().ConfigureAwait(true);
        StatusMessage = HasConsent
            ? "Consentimento registrado. Inicie uma varredura quando desejar."
            : "A varredura exige consentimento explícito para a rede local.";
    }

    private async Task GrantConsentAsync()
    {
        await _consentStore.GrantConsentAsync().ConfigureAwait(true);
        HasConsent = true;
        StatusMessage = "Consentimento registrado para a rede local deste computador.";
    }

    private async Task ScanAsync()
    {
        if (!HasConsent)
        {
            StatusMessage = "Registre o consentimento antes de iniciar a varredura.";
            return;
        }

        try
        {
            IsScanning = true;
            Progress = 0;
            Devices.Clear();
            var progress = new Progress<ActionProgress>(item => Progress = item.Percent ?? Progress);
            var devices = await _scanner.ScanAsync(progress).ConfigureAwait(true);
            foreach (var device in devices) Devices.Add(device);
            StatusMessage = $"Varredura concluída: {Devices.Count} dispositivo(s) encontrado(s).";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Falha na varredura: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}
