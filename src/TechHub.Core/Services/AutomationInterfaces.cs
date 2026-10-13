using TechHub.Core.Models;

namespace TechHub.Core.Services;

public interface IAutomationService
{
    Task<IReadOnlyList<AutomationPlaybook>> GetPlaybooksAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AutomationPlaybook playbook, CancellationToken cancellationToken = default);
    Task<AutomationRunResult> RunAsync(string playbookId, bool confirmed, string actor, CancellationToken cancellationToken = default);
}

public interface IDiagnosticAssistant
{
    Task<AssistantResponse> AskAsync(string symptom, CancellationToken cancellationToken = default);
}

public interface ISubnetCalculator
{
    SubnetCalculation Calculate(string cidr);
}
