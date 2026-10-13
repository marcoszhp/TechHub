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
    AssistantViewModel assistant) : ObservableObject
{
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

    public async Task InitializeAsync()
    {
        await Dashboard.RefreshAsync().ConfigureAwait(true);
        await Printers.RefreshAsync().ConfigureAwait(true);
        await Network.InitializeAsync().ConfigureAwait(true);
        await Diagnostics.RefreshAsync().ConfigureAwait(true);
        await Security.RefreshAsync().ConfigureAwait(true);
        await Drivers.RefreshAsync().ConfigureAwait(true);
        await Tickets.RefreshAsync().ConfigureAwait(true);
        await Software.RefreshAsync().ConfigureAwait(true);
        await Automation.RefreshAsync().ConfigureAwait(true);
    }
}
