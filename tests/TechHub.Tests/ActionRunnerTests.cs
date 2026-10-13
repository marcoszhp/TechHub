using Microsoft.Extensions.Logging.Abstractions;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Infrastructure.Actions;

namespace TechHub.Tests;

public sealed class ActionRunnerTests
{
    [Fact]
    public async Task BlocksNonReadOnlyActionWithoutConfirmation()
    {
        var audit = new InMemoryAuditStore();
        var executed = false;
        var runner = new ActionRunner(audit, new FakeRestorePointService(), new FakePrivilegeService(true), NullLogger<ActionRunner>.Instance);
        var request = CreateRequest(ActionRisk.Safe, false, (_, _) =>
        {
            executed = true;
            return Task.FromResult(new ActionResult(true, "executada", TimeSpan.Zero));
        });

        var result = await runner.ExecuteAsync(request, new ActionExecutionContext(false, false, "teste"));

        Assert.False(result.Success);
        Assert.False(executed);
        Assert.Single(audit.Entries);
        Assert.Contains("confirmação", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutesConfirmedActionAndWritesAudit()
    {
        var audit = new InMemoryAuditStore();
        var runner = new ActionRunner(audit, new FakeRestorePointService(), new FakePrivilegeService(true), NullLogger<ActionRunner>.Instance);
        var request = CreateRequest(ActionRisk.Safe, false, (_, _) =>
            Task.FromResult(new ActionResult(true, "concluída", TimeSpan.Zero)));

        var result = await runner.ExecuteAsync(request, new ActionExecutionContext(true, false, "teste"));

        Assert.True(result.Success);
        Assert.Single(audit.Entries);
        Assert.True(audit.Entries[0].Success);
        Assert.Equal("teste", audit.Entries[0].Actor);
    }

    [Fact]
    public async Task BlocksAdministratorActionWhenPrivilegeIsMissing()
    {
        var audit = new InMemoryAuditStore();
        var runner = new ActionRunner(audit, new FakeRestorePointService(), new FakePrivilegeService(false), NullLogger<ActionRunner>.Instance);
        var request = CreateRequest(ActionRisk.Elevated, true, (_, _) =>
            Task.FromResult(new ActionResult(true, "não deveria executar", TimeSpan.Zero)));

        var result = await runner.ExecuteAsync(request, new ActionExecutionContext(true, false, "teste"));

        Assert.False(result.Success);
        Assert.Contains("administrador", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ActionRequest CreateRequest(
        ActionRisk risk,
        bool requiresAdministrator,
        Func<CancellationToken, IProgress<ActionProgress>?, Task<ActionResult>> execute) =>
        new("teste", "Ação de teste", "Descrição", risk, requiresAdministrator, ["recurso de teste"], execute);

    private sealed class InMemoryAuditStore : IAuditStore
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Entries.Take(limit).ToArray());
    }

    private sealed class FakeRestorePointService : IRestorePointService
    {
        public Task<bool> TryCreateAsync(string description, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakePrivilegeService(bool isAdministrator) : IPrivilegeService
    {
        public bool IsAdministrator { get; } = isAdministrator;
    }
}
