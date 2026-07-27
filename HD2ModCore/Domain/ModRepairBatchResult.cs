namespace HD2ModCore.Domain;

// Purpose: Immutable audit information for one conservative same-key repair batch.
public sealed record ModRepairBatchResult(
    string BatchDirectory,
    string ManifestPath,
    int RequestedModCount,
    int RepairedModCount,
    int SkippedModCount,
    int FailedModCount,
    IReadOnlyList<ModRepairBatchModResult> Mods,
    int StartedModCount = 0,
    int CanceledModCount = 0,
    int NotStartedModCount = 0,
    Guid OperationId = default,
    DateTimeOffset CreatedUtc = default,
    DateTimeOffset CompletedUtc = default,
    bool ManifestWriteFailed = false,
    string? ManifestIssueCode = null)
{
    public bool HasRepairs => RepairedModCount > 0;
}

// Purpose: Describes the repair outcome for one complete Mod directory.
public sealed record ModRepairBatchModResult(
    ModNodeId NodeId,
    string Name,
    ModRepairBatchModStatus Status,
    string? Detail,
    string? CandidateDirectory,
    string? BackupDirectory,
    string? ReportPath,
    string? StageId = null,
    bool BackupCompleted = false,
    bool CommitCompleted = false,
    bool RestoreAttempted = false,
    bool RestoreCompleted = false);

public enum ModRepairBatchModStatus
{
    Repaired = 0,
    SkippedNotRepairable = 1,
    CandidateFailed = 2,
    CommitFailed = 3,
    Canceled = 4,
    NotStarted = 5,
}
