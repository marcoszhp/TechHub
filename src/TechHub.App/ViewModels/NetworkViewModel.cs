using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly INetworkConsentStore _consentStore;
    private readonly INetworkTopologyScanner? _topology;
    private CancellationTokenSource? _scanCancellation;
    private Task? _initialization;

    [ObservableProperty] private bool hasConsent;
    [ObservableProperty] private bool consentAcknowledged;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = "Carregue os adaptadores. Nenhuma varredura foi iniciada.";
    [ObservableProperty] private string scanSummary = "Nenhuma varredura realizada neste adaptador.";
    [ObservableProperty] private double progress;
    [ObservableProperty] private double zoom = 1;
    [ObservableProperty] private NetworkAdapterInfo? selectedAdapter;
    [ObservableProperty] private NetworkMapNode? selectedNode;
    [ObservableProperty] private double canvasHeight = 340;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];
    // Preserved for existing consumers; includes the locally observed configuration.
    public ObservableCollection<NetworkDeviceInfo> Devices { get; } = [];
    public ObservableCollection<NetworkMapNode> Nodes { get; } = [];
    public double CanvasWidth => 660;
    public bool CanEdit => !IsScanning && !IsRefreshing;
    public bool HasAdapter => SelectedAdapter is not null;
    public bool CanScan => CanEdit && HasAdapter && HasConsent && ConsentAcknowledged && _topology is not null;
    public string NetworkLabel => SelectedAdapter?.Cidr ?? "Nenhuma rede IPv4 ativa";
    public string AdapterSummary => SelectedAdapter is { } adapter
        ? $"Local: {adapter.LocalAddress} · Gateway: {adapter.GatewayAddress ?? "não configurado"}"
        : "Conecte um adaptador e atualize. Nenhum dispositivo foi descoberto.";
    public string ScanRangeLabel => SelectedAdapter is { } adapter
        ? $"Escopo ICMP: {adapter.ScanCidr} · {NetworkAddressing.GetScanAddresses(adapter).Count} IPs · 16 consultas simultâneas"
        : "A varredura ficará disponível após selecionar um adaptador.";
    public string ConsentLabel => HasConsent
        ? "Autorização registrada. Confirme que pode consultar a rede selecionada."
        : "Marque a autorização e registre o consentimento antes de consultar hosts.";

    public IAsyncRelayCommand GrantConsentCommand { get; }
    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand RefreshAdaptersCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand<NetworkMapNode> SelectNodeCommand { get; }
    public IRelayCommand CopyIpCommand { get; }
    public IAsyncRelayCommand ExportCsvCommand { get; }

    public NetworkViewModel(INetworkConsentStore consentStore, INetworkScanner scanner)
    {
        _consentStore = consentStore;
        _topology = scanner as INetworkTopologyScanner;
        GrantConsentCommand = new AsyncRelayCommand(GrantConsentAsync, () => CanEdit && ConsentAcknowledged && !HasConsent);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => CanScan);
        RefreshAdaptersCommand = new AsyncRelayCommand(RefreshAdaptersAsync, () => CanEdit);
        CancelCommand = new RelayCommand(CancelScan, () => IsScanning);
        SelectNodeCommand = new RelayCommand<NetworkMapNode>(node => SelectedNode = node);
        CopyIpCommand = new RelayCommand(CopyIp, () => SelectedNode is not null);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => CanEdit && Devices.Count > 0);
    }

    // MainViewModel and the view can both request initialization; inventory never sends probes.
    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try { HasConsent = await _consentStore.HasConsentAsync(); }
        catch (Exception ex) { StatusMessage = "Falha ao ler consentimento: " + ex.Message; }
        await RefreshAdaptersAsync();
    }

    private async Task RefreshAdaptersAsync()
    {
        if (!CanEdit) return;
        IsRefreshing = true;
        try
        {
            if (_topology is null)
                throw new InvalidOperationException("O scanner registrado precisa implementar INetworkTopologyScanner.");
            var previous = SelectedAdapter;
            // Local OS inventory only; no ping, ARP process or DNS lookup.
            var adapters = await _topology.GetAdaptersAsync();
            Adapters.Clear();
            foreach (var adapter in adapters) Adapters.Add(adapter);
            SelectedAdapter = null;
            SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == previous?.Id && a.LocalAddress == previous.LocalAddress)
                              ?? Adapters.FirstOrDefault();
            StatusMessage = HasAdapter
                ? "Configuração local carregada. Confirme a autorização e inicie quando desejar."
                : "Nenhum adaptador IPv4 ativo. Conecte uma rede e atualize os adaptadores.";
        }
        catch (Exception ex)
        {
            Adapters.Clear();
            SelectedAdapter = null;
            StatusMessage = "Falha ao carregar adaptadores: " + ex.Message;
        }
        finally { IsRefreshing = false; }
    }

    private async Task GrantConsentAsync()
    {
        if (!ConsentAcknowledged || !CanEdit) return;
        IsRefreshing = true;
        try
        {
            await _consentStore.GrantConsentAsync();
            HasConsent = true;
            StatusMessage = "Consentimento registrado. A varredura começa somente ao clicar em Varrer.";
        }
        catch (Exception ex) { StatusMessage = "Falha ao registrar consentimento: " + ex.Message; }
        finally { IsRefreshing = false; }
    }

    private async Task ScanAsync()
    {
        if (!CanScan || SelectedAdapter is not { } adapter || _topology is null) return;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        IsScanning = true;
        Progress = 0;
        RebuildNodes([]);
        ScanSummary = "Varredura em andamento; o mapa mostra a configuração local.";
        StatusMessage = "Iniciando varredura autorizada…";
        try
        {
            var reporter = new Progress<ActionProgress>(item =>
            {
                // Ignore queued reports from a finished/cancelled operation.
                if (!IsScanning || !ReferenceEquals(_scanCancellation, cancellation) || cancellation.IsCancellationRequested) return;
                Progress = Math.Max(Progress, item.Percent ?? Progress);
                StatusMessage = item.Message;
            });
            var result = await _topology.ScanAsync(adapter, reporter, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            RebuildNodes(result.Devices);
            var found = result.Devices.Count;
            ScanSummary = $"Última varredura: {DateTimeOffset.Now:HH:mm:ss} · {result.AttemptedAddresses} IPs consultados · {found} host(s) com evidência.";
            Progress = 100;
            StatusMessage = found == 0
                ? "Concluída sem hosts descobertos. Isso não comprova ausência de dispositivos; ICMP pode estar bloqueado."
                : "Concluída. ARP pode estar desatualizado; DNS e OUI não confirmam identidade ou tipo.";
            if (result.Warnings.Count > 0) StatusMessage += " " + string.Join(" ", result.Warnings);
        }
        catch (OperationCanceledException)
        {
            ScanSummary = "Varredura cancelada; resultados parciais não foram publicados.";
            StatusMessage = "Cancelada. Somente a configuração local está visível.";
        }
        catch (Exception ex)
        {
            ScanSummary = "A varredura não foi concluída.";
            StatusMessage = "Falha na varredura: " + ex.Message;
        }
        finally
        {
            _scanCancellation = null;
            IsScanning = false;
        }
    }

    public void CancelScan()
    {
        _scanCancellation?.Cancel();
        if (IsScanning) StatusMessage = "Cancelando consultas…";
    }

    private void RebuildNodes(IReadOnlyList<NetworkDeviceInfo> discoveries)
    {
        var selectedIp = SelectedNode?.Device.IpAddress;
        SelectedNode = null;
        Devices.Clear();
        Nodes.Clear();
        if (SelectedAdapter is not { } adapter)
        {
            CanvasHeight = 340;
            NotifyCommands();
            return;
        }

        Devices.Add(new NetworkDeviceInfo(adapter.LocalAddress, adapter.MacAddress, "Não identificado", Environment.MachineName,
            "Este computador", false, null, DateTimeOffset.UtcNow)
        { Role = NetworkDeviceRole.Local, Evidence = "Configuração IPv4 local observada no Windows; sem consulta ICMP" });
        if (adapter.GatewayAddress is { } gateway && gateway != adapter.LocalAddress && NetworkAddressing.IsUnicast(gateway))
        {
            var observed = discoveries.FirstOrDefault(d => d.IpAddress == gateway);
            Devices.Add(observed is null
                ? new NetworkDeviceInfo(gateway, "", "Não identificado", "", "Gateway configurado", false, null, DateTimeOffset.UtcNow)
                  { Role = NetworkDeviceRole.Gateway, Evidence = "Gateway configurado no Windows; não consultado" }
                : observed with { Role = NetworkDeviceRole.Gateway, DeviceType = "Gateway configurado",
                    Evidence = "Gateway configurado no Windows; " + observed.Evidence });
        }
        foreach (var device in discoveries.Where(d => d.IpAddress != adapter.LocalAddress && d.IpAddress != adapter.GatewayAddress))
            Devices.Add(device);

        for (var index = 0; index < Devices.Count; index++)
        {
            // Stable readable grid; dashed lines mean membership of the logical subnet only.
            var x = 20 + index % 3 * 215;
            var y = 160 + index / 3 * 145;
            Nodes.Add(new NetworkMapNode(Devices[index], x, y));
        }
        CanvasHeight = Math.Max(340, 160 + Math.Ceiling(Nodes.Count / 3d) * 145 + 20);
        SelectedNode = Nodes.FirstOrDefault(n => n.Device.IpAddress == selectedIp) ?? Nodes.FirstOrDefault();
        NotifyCommands();
    }

    private void CopyIp()
    {
        if (SelectedNode is null) return;
        try
        {
            Clipboard.SetText(SelectedNode.Device.IpAddress);
            StatusMessage = "IP copiado: " + SelectedNode.Device.IpAddress;
        }
        catch (Exception ex) { StatusMessage = "Não foi possível copiar o IP: " + ex.Message; }
    }

    private async Task ExportCsvAsync()
    {
        if (!CanEdit || Devices.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "Exportar observações da rede", Filter = "CSV (*.csv)|*.csv", DefaultExt = ".csv",
            AddExtension = true, OverwritePrompt = true, FileName = "TechHub-rede.csv"
        };
        try
        {
            if (dialog.ShowDialog() != true) return;
            var csv = NetworkCsv.Serialize(Devices);
            await File.WriteAllTextAsync(dialog.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = $"CSV exportado: {Devices.Count} observações. Inclui configuração local e evidência de cada host.";
        }
        catch (Exception ex) { StatusMessage = "Falha ao exportar CSV: " + ex.Message; }
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value)
    {
        ConsentAcknowledged = false;
        Progress = 0;
        ScanSummary = "Nenhuma varredura realizada neste adaptador.";
        RebuildNodes([]);
        OnPropertyChanged(nameof(NetworkLabel));
        OnPropertyChanged(nameof(AdapterSummary));
        OnPropertyChanged(nameof(ScanRangeLabel));
        OnPropertyChanged(nameof(HasAdapter));
        StatusMessage = value is null ? "Nenhuma rede selecionada." : "Configuração local observada; nenhum scan realizado.";
        NotifyCommands();
    }

    partial void OnSelectedNodeChanged(NetworkMapNode? value)
    {
        foreach (var node in Nodes) node.IsSelected = ReferenceEquals(node, value);
        CopyIpCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasConsentChanged(bool value)
    {
        OnPropertyChanged(nameof(ConsentLabel));
        NotifyCommands();
    }
    partial void OnConsentAcknowledgedChanged(bool value) => NotifyCommands();
    partial void OnIsScanningChanged(bool value) => NotifyCommands();
    partial void OnIsRefreshingChanged(bool value) => NotifyCommands();

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanScan));
        ScanCommand.NotifyCanExecuteChanged();
        GrantConsentCommand.NotifyCanExecuteChanged();
        RefreshAdaptersCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class NetworkMapNode(NetworkDeviceInfo device, double x, double y) : ObservableObject
{
    [ObservableProperty] private bool isSelected;
    public NetworkDeviceInfo Device { get; } = device;
    public double X { get; } = x;
    public double Y { get; } = y;
    public double CenterX => X + 95;
    public double CenterY => Y;
    public string Title => Device.Role switch
    {
        NetworkDeviceRole.Local => "Este computador",
        NetworkDeviceRole.Gateway => "Gateway configurado",
        _ => string.IsNullOrWhiteSpace(Device.HostName) ? "Host observado" : Device.HostName
    };
    public string NameText => string.IsNullOrWhiteSpace(Device.HostName) ? "Não resolvido" : Device.HostName;
    public string MacText => string.IsNullOrWhiteSpace(Device.MacAddress) ? "Não observado" : Device.MacAddress;
    public string LatencyText => Device.WasProbed && Device.LatencyMilliseconds is { } latency ? $"{latency} ms" : "Não medida";
    public string ReachabilityText => !Device.WasProbed ? "Não consultado via ICMP"
        : Device.IsReachable ? "Respondeu ao ICMP" : "Sem resposta ICMP";
    public string Accent => Device.Role == NetworkDeviceRole.Local ? "#2DD4BF" : Device.Role == NetworkDeviceRole.Gateway ? "#60A5FA" : "#93C5FD";
    public string AccessibleName => $"{Title}, IP {Device.IpAddress}, {ReachabilityText}. {Device.Evidence}";
}
