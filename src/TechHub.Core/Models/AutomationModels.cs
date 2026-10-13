namespace TechHub.Core.Models;

public enum AutomationActionKind
{
    CollectDashboard,
    ExportTicketsCsv
}

public sealed record AutomationStep(string Id, string Name, AutomationActionKind Action, bool RequiresConfirmation);

public sealed record AutomationPlaybook(string Id, string Name, string Description, IReadOnlyList<AutomationStep> Steps, bool Enabled = true);

public sealed record AutomationRunResult(bool Success, string Message, IReadOnlyList<string> Outputs);

public sealed record AssistantResponse(string Symptom, string Summary, IReadOnlyList<string> Questions, IReadOnlyList<string> RecommendedChecks, IReadOnlyList<string> SafeActions);

public sealed record SubnetCalculation(string Network, string Broadcast, string FirstHost, string LastHost, long HostCount, int PrefixLength);
