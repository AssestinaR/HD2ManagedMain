namespace HD2ModCore.Domain;

// Purpose: Summarizes whether a flat library Mod can be rebuilt against current same-AssetKey game-data Units.
public sealed record ModSameKeyReconstructionState(
	ModNodeId SourceNodeId,
	string? SourcePatchTocPath,
	SameKeyReconstructionPlan? Plan,
	bool IsGameDataIndexCurrent,
	int ReplacementUnitCount,
	int MinifyOnlyUnitCount,
	int ReplacementMeshCount,
	int MinifiedMeshCount,
	int SharedTargetUnitCount,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool CanWrite => SourcePatchTocPath is not null
		&& IsGameDataIndexCurrent
		&& Plan is { SourceUnitCount: > 0 }
		&& Issues.All(issue => issue.Severity != CoreIssueSeverity.Error);
}

// Purpose: Describes the written test-copy directory and validation evidence without altering the source Mod.
public sealed record SameKeyReconstructionOperationResult(
	bool IsSuccessful,
	string? OutputDirectory,
	string? ModelDirectory,
	string? ReportJsonPath,
	string? ReportMarkdownPath,
	int OutputUnitCount,
	int ReplacementUnitCount,
	int MinifyOnlyUnitCount,
	int ReplacementMeshCount,
	int MinifiedMeshCount,
	IReadOnlyList<CoreIssue> Issues);
