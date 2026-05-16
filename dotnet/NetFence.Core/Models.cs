namespace NetFence.Core;

public enum FirewallDirection
{
    Inbound,
    Outbound
}

public enum NetworkMode
{
    BlockAll,
    AllowAll,
    LanOnly,
    Custom
}

public sealed record ProcessRow(
    int ProcessId,
    int ParentProcessId,
    string ProcessName,
    string? ExecutablePath,
    string? CommandLine);

public sealed record RelatedCandidate(
    bool Selected,
    string Program,
    string Reason,
    int ProcessId,
    string ProcessName);

public sealed record FirewallRuleInfo(
    string ProfileName,
    string DisplayName,
    string Direction,
    bool Enabled,
    string Action,
    string Program);

public sealed record FirewallProgramTarget(string ProfileName, string Program);

public sealed record SnapshotExportResult(string Path, int RowCount);

public sealed record NetworkConnection(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    bool IsBlockedByNetFence);

public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string StartMode,
    string? ExecutablePath,
    bool IsSystemService);

public sealed record ScheduledTaskInfo(
    string Name,
    string Path,
    string State,
    string Triggers,
    string Actions,
    string? ExecutablePath);
