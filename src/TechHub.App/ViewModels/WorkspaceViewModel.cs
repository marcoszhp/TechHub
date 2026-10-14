using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed record WorkspaceDiagnosticChoice(WorkspaceDiagnostic Kind, string Label);

public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly IComputerInventoryService computer;
    private readonly IWorkspaceToolsService tools;
    private readonly IWorkspaceClientStore clients;
    private readonly IWorkspaceBackupService backup;
    private readonly IAuditStore audit;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private int equipmentLoad;

    [ObservableProperty] private string section = "computer";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Selecione Atualizar para carregar os dados.";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private string computerTimestamp = "Ainda não coletado";
    [ObservableProperty] private string consoleOutput = "";
    [ObservableProperty] private string consoleStatus = "Pronto. A consulta de rede só começa ao clicar em Executar.";
    [ObservableProperty] private bool isDiagnosticRunning;
    [ObservableProperty] private string diagnosticHost = "";
    [ObservableProperty] private WorkspaceDiagnosticChoice selectedDiagnostic;
    [ObservableProperty] private string clientSearch = "";
    [ObservableProperty] private string equipmentSearch = "";
    [ObservableProperty] private WorkspaceClient? selectedClient;
    [ObservableProperty] private WorkspaceEquipment? selectedEquipment;
    [ObservableProperty] private string clientName = "";
    [ObservableProperty] private string clientContact = "";
    [ObservableProperty] private string clientNotes = "";
    [ObservableProperty] private string equipmentName = "";
    [ObservableProperty] private string equipmentSerial = "";
    [ObservableProperty] private string equipmentNotes = "";

    public ApplicationPaths Paths { get; }
    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Não disponível";
    public string Mode => Paths.Mode == StorageMode.Portable ? "Portátil" : "Instalado";
    public string SectionTitle => Section switch
    {
        "computer" => "Informações do computador", "tools" => "Ferramentas e console",
        "clients" => "Clientes e equipamentos", "history" => "Histórico de execução",
        "backup" => "Backup e restauração", "settings" => "Configurações", _ => "Workspace"
    };
    public IReadOnlyList<WorkspaceTool> Tools => tools.Tools;
    public IReadOnlyList<WorkspaceDiagnosticChoice> Diagnostics { get; } =
    [
        new(WorkspaceDiagnostic.IpConfiguration, "Configuração IP (ipconfig /all)"),
        new(WorkspaceDiagnostic.PingGateway, "Ping do gateway IPv4"),
        new(WorkspaceDiagnostic.PingHost, "Ping de um host"),
        new(WorkspaceDiagnostic.NameLookup, "Consulta DNS (nslookup)")
    ];
    public ObservableCollection<ComputerDetail> ComputerDetails { get; } = [];
    public ObservableCollection<string> ComputerWarnings { get; } = [];
    public ObservableCollection<WorkspaceClient> Clients { get; } = [];
    public ObservableCollection<WorkspaceEquipment> Equipment { get; } = [];
    public ObservableCollection<AuditEntry> History { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand<string> LaunchToolCommand { get; }
    public IAsyncRelayCommand RunDiagnosticCommand { get; }
    public IRelayCommand CancelDiagnosticCommand { get; }
    public IRelayCommand NewClientCommand { get; }
    public IRelayCommand NewEquipmentCommand { get; }
    public IAsyncRelayCommand SaveClientCommand { get; }
    public IAsyncRelayCommand SaveEquipmentCommand { get; }
    public IAsyncRelayCommand SearchEquipmentCommand { get; }
    public IAsyncRelayCommand ExportBackupCommand { get; }
    public IAsyncRelayCommand RestoreBackupCommand { get; }
    public IRelayCommand OpenDataCommand { get; }
    public IRelayCommand OpenLogsCommand { get; }

    public WorkspaceViewModel(IComputerInventoryService computer, IWorkspaceToolsService tools,
        IWorkspaceClientStore clients, IWorkspaceBackupService backup, IAuditStore audit, IApplicationPaths paths)
    {
        this.computer = computer; this.tools = tools; this.clients = clients;
        this.backup = backup; this.audit = audit; Paths = paths.Current;
        selectedDiagnostic = Diagnostics[0];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        LaunchToolCommand = new RelayCommand<string>(LaunchTool);
        RunDiagnosticCommand = new AsyncRelayCommand(RunDiagnosticAsync);
        CancelDiagnosticCommand = new RelayCommand(() => RunDiagnosticCommand.Cancel());
        NewClientCommand = new RelayCommand(() => { SelectedClient = null; ClientName = ClientContact = ClientNotes = ""; });
        NewEquipmentCommand = new RelayCommand(() => { SelectedEquipment = null; EquipmentName = EquipmentSerial = EquipmentNotes = ""; });
        SaveClientCommand = new AsyncRelayCommand(SaveClientAsync);
        SaveEquipmentCommand = new AsyncRelayCommand(SaveEquipmentAsync);
        SearchEquipmentCommand = new AsyncRelayCommand(() => LoadEquipmentAsync(SelectedClient));
        ExportBackupCommand = new AsyncRelayCommand(ExportBackupAsync);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync);
        OpenDataCommand = new RelayCommand(() => OpenFolder(Paths.DataDirectory));
        OpenLogsCommand = new RelayCommand(() => OpenFolder(Paths.LogsDirectory));
    }

    /// <summary>Shell navigation contract. A singleton VM can back separate view instances in each TabItem.</summary>
    public async Task SetSectionAsync(string section)
    {
        var normalized = section.Trim().ToLowerInvariant();
        if (normalized == "console") normalized = "tools";
        if (normalized is not ("computer" or "tools" or "clients" or "history" or "backup" or "settings"))
            throw new ArgumentException("Seção Workspace inválida.", nameof(section));
        Section = normalized;
        await RefreshAsync();
    }

    public void SetSection(string section) => _ = SetSectionAsync(section);
    partial void OnSectionChanged(string value) => OnPropertyChanged(nameof(SectionTitle));

    public async Task RefreshAsync()
    {
        await refreshGate.WaitAsync();
        try
        {
            IsBusy = true; ErrorMessage = ""; StatusMessage = "Carregando…";
            switch (Section)
            {
                case "computer":
                    var info = await computer.GetAsync();
                    Replace(ComputerDetails, info.Details); Replace(ComputerWarnings, info.Warnings);
                    ComputerTimestamp = $"Coletado em {info.CollectedAt:dd/MM/yyyy HH:mm:ss}";
                    StatusMessage = info.Warnings.Count == 0 ? "Inventário local atualizado." : "Inventário parcial. Consulte os avisos de coleta.";
                    break;
                case "clients": await RefreshClientsAsync(); StatusMessage = $"{Clients.Count} cliente(s). Selecione um para ver os equipamentos."; break;
                case "history":
                    Replace(History, await Task.Run(() => audit.GetRecentAsync(200)));
                    StatusMessage = History.Count == 0 ? "Nenhuma execução registrada." : $"Últimos {History.Count} registros de auditoria.";
                    break;
                default: StatusMessage = "Pronto."; break;
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; StatusMessage = "Falha ao atualizar. Os últimos dados exibidos podem estar desatualizados."; }
        finally { IsBusy = false; refreshGate.Release(); }
    }

    private async Task RefreshClientsAsync()
    {
        var id = SelectedClient?.Id;
        var found = await clients.FindClientsAsync(ClientSearch);
        Replace(Clients, found);
        SelectedClient = found.FirstOrDefault(c => c.Id == id);
    }

    partial void OnSelectedClientChanged(WorkspaceClient? value)
    {
        ClientName = value?.Name ?? ""; ClientContact = value?.Contact ?? ""; ClientNotes = value?.Notes ?? "";
        _ = LoadEquipmentAsync(value);
    }

    partial void OnSelectedEquipmentChanged(WorkspaceEquipment? value)
    {
        EquipmentName = value?.Name ?? ""; EquipmentSerial = value?.SerialNumber ?? ""; EquipmentNotes = value?.Notes ?? "";
    }

    private async Task LoadEquipmentAsync(WorkspaceClient? client)
    {
        var generation = ++equipmentLoad;
        Equipment.Clear(); SelectedEquipment = null;
        if (client is null) return;
        try
        {
            var result = await clients.FindEquipmentAsync(client.Id, EquipmentSearch);
            if (generation == equipmentLoad && SelectedClient?.Id == client.Id) Replace(Equipment, result);
        }
        catch (Exception ex) { if (generation == equipmentLoad) ErrorMessage = $"Equipamentos: {ex.Message}"; }
    }

    private Task SaveClientAsync() => PerformAsync(async () =>
    {
        var saved = await clients.SaveClientAsync(new(SelectedClient?.Id ?? 0, ClientName, ClientContact, ClientNotes));
        ClientSearch = "";
        await RefreshClientsAsync(); SelectedClient = Clients.First(c => c.Id == saved.Id);
        StatusMessage = $"Cliente #{saved.Id} salvo.";
    });

    private Task SaveEquipmentAsync() => PerformAsync(async () =>
    {
        var owner = SelectedClient ?? throw new InvalidOperationException("Selecione e salve um cliente primeiro.");
        var saved = await clients.SaveEquipmentAsync(new(SelectedEquipment?.Id ?? 0, owner.Id, EquipmentName, EquipmentSerial, EquipmentNotes));
        EquipmentSearch = "";
        await LoadEquipmentAsync(SelectedClient);
        SelectedEquipment = Equipment.FirstOrDefault(e => e.Id == saved.Id);
        StatusMessage = $"Equipamento #{saved.Id} salvo para {owner.Name}.";
    });

    private void LaunchTool(string? id)
    {
        try { ErrorMessage = ""; tools.Launch(id ?? ""); StatusMessage = "Solicitação enviada ao Windows. A ferramenta abre em outra janela."; }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private async Task RunDiagnosticAsync(CancellationToken cancellationToken)
    {
        IsDiagnosticRunning = true; ConsoleOutput = ""; ErrorMessage = "";
        ConsoleStatus = "Executando… limite de 15 segundos. Cancelar interrompe o processo.";
        var progress = new Progress<string>(chunk => ConsoleOutput += chunk);
        try
        {
            var result = await tools.RunAsync(SelectedDiagnostic.Kind, DiagnosticHost, progress, cancellationToken);
            ConsoleStatus = $"{result.Message} ({result.Duration.TotalSeconds:N1}s)";
            // Progress callbacks already queued on the dispatcher are drained before the completion continuation.
        }
        catch (OperationCanceledException) { ConsoleStatus = "Diagnóstico cancelado."; }
        catch (Exception ex) { ErrorMessage = ex.Message; ConsoleStatus = "Diagnóstico não executado."; }
        finally { IsDiagnosticRunning = false; }
    }

    private async Task ExportBackupAsync()
    {
        var dialog = new SaveFileDialog { Title = "Exportar backup TechHub fora da pasta Data", Filter = "Backup ZIP (*.zip)|*.zip", DefaultExt = ".zip", FileName = $"TechHub-{DateTime.Now:yyyyMMdd-HHmmss}.zip", OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        await PerformAsync(async () =>
        {
            var result = await backup.ExportAsync(dialog.FileName);
            StatusMessage = $"Backup exportado: {result.Path} · {result.SettingsFiles} arquivo(s) JSON · {result.Bytes / 1024d:N0} KB.";
        });
    }

    private async Task RestoreBackupAsync()
    {
        var file = new OpenFileDialog { Title = "Selecionar backup TechHub", Filter = "Backup ZIP (*.zip)|*.zip", CheckFileExists = true };
        if (file.ShowDialog() != true) return;
        var folder = new OpenFolderDialog { Title = "Crie e selecione uma pasta NOVA e VAZIA fora dos dados ativos", Multiselect = false };
        if (folder.ShowDialog() != true) return;
        await PerformAsync(async () =>
        {
            var result = await backup.RestoreAsync(file.FileName, folder.FolderName);
            StatusMessage = $"Cópia restaurada em {result.Path}. Os dados em uso continuam em {Paths.DataDirectory}; a aplicação não troca automaticamente de banco.";
        });
    }

    private async Task PerformAsync(Func<Task> operation)
    {
        await refreshGate.WaitAsync();
        try { IsBusy = true; ErrorMessage = ""; StatusMessage = "Processando…"; await operation(); }
        catch (Exception ex) { ErrorMessage = ex.Message; StatusMessage = "Operação não concluída."; }
        finally { IsBusy = false; refreshGate.Release(); }
    }

    private void OpenFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Pasta ainda não existe.");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            start.ArgumentList.Add(folder);
            using var process = Process.Start(start);
            ErrorMessage = "";
        }
        catch (Exception ex) { ErrorMessage = $"Não foi possível abrir a pasta: {ex.Message}"; }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    { collection.Clear(); foreach (var item in items) collection.Add(item); }
}
