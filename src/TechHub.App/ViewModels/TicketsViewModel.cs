using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.App.ViewModels;

public sealed partial class TicketsViewModel : ObservableObject
{
    private readonly ITicketService _service;

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private TicketCategory category = TicketCategory.Outro;
    [ObservableProperty] private TicketPriority priority = TicketPriority.Normal;
    [ObservableProperty] private string requester = Environment.UserName;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private SupportTicket? selectedTicket;
    [ObservableProperty] private string comment = string.Empty;

    public IReadOnlyList<TicketCategory> Categories { get; } = Enum.GetValues<TicketCategory>();
    public IReadOnlyList<TicketPriority> Priorities { get; } = Enum.GetValues<TicketPriority>();
    public IReadOnlyList<TicketStatus> Statuses { get; } = Enum.GetValues<TicketStatus>();
    public ObservableCollection<SupportTicket> Tickets { get; } = [];
    public ObservableCollection<TicketComment> Comments { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CreateCommand { get; }
    public IAsyncRelayCommand AddCommentCommand { get; }
    public IAsyncRelayCommand CollectDiagnosticsCommand { get; }
    public IAsyncRelayCommand SetResolvedCommand { get; }

    public TicketsViewModel(ITicketService service)
    {
        _service = service;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateCommand = new AsyncRelayCommand(CreateAsync);
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync);
        CollectDiagnosticsCommand = new AsyncRelayCommand(CollectDiagnosticsAsync);
        SetResolvedCommand = new AsyncRelayCommand(SetResolvedAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var selectedId = SelectedTicket?.Id;
            var refreshed = await _service.GetTicketsAsync().ConfigureAwait(true);
            Tickets.Clear();
            foreach (var ticket in refreshed) Tickets.Add(ticket);
            SelectedTicket = Tickets.FirstOrDefault(ticket => ticket.Id == selectedId);
            StatusMessage = $"{Tickets.Count} chamado(s) carregado(s).";
        }
        catch (Exception exception) { StatusMessage = $"Falha ao carregar chamados: {exception.Message}"; }
    }

    partial void OnSelectedTicketChanged(SupportTicket? value)
    {
        _ = LoadCommentsAsync(value);
    }

    private async Task LoadCommentsAsync(SupportTicket? ticket)
    {
        Comments.Clear();
        if (ticket is null) return;
        try
        {
            var comments = await _service.GetCommentsAsync(ticket.Id).ConfigureAwait(true);
            if (!ReferenceEquals(SelectedTicket, ticket)) return;
            Comments.Clear();
            foreach (var comment in comments) Comments.Add(comment);
        }
        catch (Exception exception) { StatusMessage = $"Falha ao carregar comentários: {exception.Message}"; }
    }

    private async Task CreateAsync()
    {
        try
        {
            var ticket = await _service.CreateAsync(new CreateTicketRequest(Title, Description, Category, Priority, Requester)).ConfigureAwait(true);
            Tickets.Insert(0, ticket);
            SelectedTicket = ticket;
            Title = string.Empty;
            Description = string.Empty;
            StatusMessage = $"Chamado #{ticket.Id} criado.";
        }
        catch (Exception exception) { StatusMessage = $"Chamado não criado: {exception.Message}"; }
    }

    private async Task AddCommentAsync()
    {
        if (SelectedTicket is null || string.IsNullOrWhiteSpace(Comment)) { StatusMessage = "Selecione um chamado e informe um comentário."; return; }
        try
        {
            var item = await _service.AddCommentAsync(SelectedTicket.Id, Environment.UserName, Comment).ConfigureAwait(true);
            Comments.Add(item);
            Comment = string.Empty;
            StatusMessage = "Comentário adicionado.";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { StatusMessage = $"Comentário não adicionado: {exception.Message}"; }
    }

    private async Task CollectDiagnosticsAsync()
    {
        if (SelectedTicket is null) { StatusMessage = "Selecione um chamado."; return; }
        try { var path = await _service.CollectDiagnosticsAsync(SelectedTicket.Id).ConfigureAwait(true); await RefreshAsync().ConfigureAwait(true); StatusMessage = $"Diagnóstico anexado: {path}"; }
        catch (Exception exception) { StatusMessage = $"Diagnóstico não coletado: {exception.Message}"; }
    }

    private async Task SetResolvedAsync()
    {
        if (SelectedTicket is null) { StatusMessage = "Selecione um chamado."; return; }
        try { SelectedTicket = await _service.SetStatusAsync(SelectedTicket.Id, TicketStatus.Resolvido).ConfigureAwait(true); await RefreshAsync().ConfigureAwait(true); StatusMessage = "Chamado marcado como resolvido."; }
        catch (Exception exception) { StatusMessage = $"Status não alterado: {exception.Message}"; }
    }
}
