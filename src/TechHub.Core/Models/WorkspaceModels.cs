namespace TechHub.Core.Models;

public sealed record ComputerDetail(string Label, string Value);
public sealed record ComputerInventory(DateTimeOffset CollectedAt, IReadOnlyList<ComputerDetail> Details,
    IReadOnlyList<string> Warnings);
public sealed record WorkspaceTool(string Id, string Name, string Description);
public enum WorkspaceDiagnostic { IpConfiguration, PingGateway, PingHost, NameLookup }
public sealed record DiagnosticCompletion(int? ExitCode, bool Cancelled, bool TimedOut,
    string Output, string Message, TimeSpan Duration);
public sealed record WorkspaceClient(long Id, string Name, string Contact, string Notes);
public sealed record WorkspaceEquipment(long Id, long ClientId, string Name, string SerialNumber, string Notes);
public sealed record WorkspaceBackupResult(string Path, int SettingsFiles, long Bytes);
