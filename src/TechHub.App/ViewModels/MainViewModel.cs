using CommunityToolkit.Mvvm.ComponentModel;
using TechHub.Core.Models;

namespace TechHub.App.ViewModels;

public sealed class MainViewModel(
    DashboardViewModel dashboard,
    PrintersViewModel printers,
    NetworkViewModel network,
    DiagnosticsViewModel diagnostics,
    SecurityViewModel security,
    DriversViewModel drivers,
    CleaningViewModel cleaning,
    TicketsViewModel tickets,
    ReportsViewModel reports,
    SoftwareViewModel software,
    AutomationViewModel automation,
    AssistantViewModel assistant,
    WorkspaceViewModel workspace) : ObservableObject
{
    private readonly HashSet<string> _loaded = [];
    private readonly HashSet<string> _loading = [];
    private string _currentModule = "dashboard";
    private string _loadStatus = "Pronto · dados armazenados localmente";
    public string LoadStatus { get => _loadStatus; private set => SetProperty(ref _loadStatus, value); }
    public WorkspaceViewModel Workspace { get; } = workspace;
    public DashboardViewModel Dashboard { get; } = dashboard;
    public PrintersViewModel Printers { get; } = printers;
    public NetworkViewModel Network { get; } = network;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
    public SecurityViewModel Security { get; } = security;
    public DriversViewModel Drivers { get; } = drivers;
    public CleaningViewModel Cleaning { get; } = cleaning;
    public TicketsViewModel Tickets { get; } = tickets;
    public ReportsViewModel Reports { get; } = reports;
    public SoftwareViewModel Software { get; } = software;
    public AutomationViewModel Automation { get; } = automation;
    public AssistantViewModel Assistant { get; } = assistant;

    public IReadOnlyList<ModuleDefinition> Modules { get; } =
    [
        new("dashboard", "Dashboard", "Visão geral da saúde do computador"),
        new("printers", "Impressoras", "Inventário e instalação por IP"),
        new("network", "Rede", "Descoberta consentida da rede local"),
        new("diagnostics", "Diagnóstico", "Integridade do Windows e reparos guiados"),
        new("security", "Segurança", "Defender, firewall e proteção do dispositivo"),
        new("drivers", "Drivers", "Inventário, verificação e backup"),
        new("cleaning", "Limpeza", "Prévia segura de arquivos temporários"),
        new("tickets", "Atendimento", "Chamados e coleta de diagnóstico"),
        new("reports", "Relatórios", "Exportação de chamados"),
        new("software", "Software", "Inventário e atualizações"),
        new("automation", "Automação", "Playbooks locais aprovados"),
        new("assistant", "Assistente", "Diagnóstico offline por regras")
    ];

    public Task InitializeAsync() => LoadModuleAsync("dashboard");

    public async Task LoadModuleAsync(string module)
    {
        _currentModule = module;
        if (module is "computer" or "tools" or "clients" or "history" or "backup" or "settings")
        {
            try { await Workspace.SetSectionAsync(module); }
            catch (Exception ex) { LoadStatus = $"Não foi possível carregar o módulo: {ex.Message}"; }
            return;
        }
        if (_loading.Contains(module)) { LoadStatus = "Consulta em andamento… você pode continuar navegando."; return; }
        if (_loaded.Contains(module)) { LoadStatus = "Pronto · use Atualizar para consultar novamente"; return; }
        _loading.Add(module);
        LoadStatus = "Consultando dados locais… você pode continuar navegando.";
        try
        {
            switch (module)
            {
                case "dashboard": await Dashboard.RefreshAsync(); break;
                case "printers": await Printers.RefreshAsync(); break;
                case "network": await Network.InitializeAsync(); break;
                case "diagnostics": await Diagnostics.RefreshAsync(); break;
                case "security": await Security.RefreshAsync(); break;
                case "drivers": await Drivers.RefreshAsync(); break;
                case "tickets": await Tickets.RefreshAsync(); break;
                case "software": await Software.RefreshAsync(); break;
                case "automation": await Automation.RefreshAsync(); break;
            }
            _loaded.Add(module);
            if (_currentModule == module) LoadStatus = "Pronto · consulte o estado da operação na área de trabalho";
        }
        catch (Exception ex)
        {
            if (_currentModule == module) LoadStatus = $"Falha ao carregar: {ex.Message}. Selecione novamente para tentar.";
        }
        finally { _loading.Remove(module); }
    }
}
