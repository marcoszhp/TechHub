using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public string ApplicationVersion { get; } =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "desenvolvimento";
    public string ApplicationVersionLabel => $"TechHub {ApplicationVersion} · Ações de sistema exigem confirmação";
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

    public bool HasActiveWorkForUpdate()
    {
        if (_loading.Count != 0 || Diagnostics.IsBusy || Drivers.IsBusy || Cleaning.IsBusy ||
            Network.IsScanning || Network.IsRefreshing || Workspace.IsBusy || Workspace.IsDiagnosticRunning ||
            Dashboard.RefreshCommand.IsRunning) return true;
        object[] modules = [Printers, Network, Diagnostics, Security, Drivers, Cleaning, Tickets, Reports, Software, Automation, Assistant, Workspace];
        // Covers every public async action, including new module commands, without invoking actions.
        return modules.SelectMany(module => module.GetType().GetProperties()
                .Where(property => typeof(IAsyncRelayCommand).IsAssignableFrom(property.PropertyType))
                .Select(property => property.GetValue(module)))
            .OfType<IAsyncRelayCommand>().Any(command => command.IsRunning);
    }

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
        // A failed manual refresh must not leave a module permanently marked as loaded.
        if (module == "diagnostics" && !Diagnostics.LastRefreshSucceeded ||
            module == "drivers" && !Drivers.LastRefreshSucceeded)
            _loaded.Remove(module);
        if (_loaded.Contains(module)) { LoadStatus = "Pronto · use Atualizar dados para consultar novamente"; return; }
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
            if (module == "diagnostics" && !Diagnostics.LastRefreshSucceeded ||
                module == "drivers" && !Drivers.LastRefreshSucceeded)
            {
                if (_currentModule == module)
                    LoadStatus = "Consulta incompleta · consulte o aviso do módulo e tente novamente em Atualizar dados.";
                return;
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
