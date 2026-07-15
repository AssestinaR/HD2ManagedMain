namespace HD2ModCore.Domain;

// Purpose: Carries the read-only feasibility result for rebuilding each source Unit against its current same-key game-data target.
public sealed record SameKeyReconstructionRequest(
	string SourcePatchTocPath,
	string GameDataDirectory,
	bool AllowExperimentalCandidates = false)
{
	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(GameDataDirectory);
	}
}

public sealed record SameKeyReconstructionPlan(
	SameKeyReconstructionRequest Request,
	IReadOnlyList<SameKeyUnitReconstructionPlan> Units,
	IReadOnlyList<CoreIssue> Issues)
{
	public int SourceUnitCount => Units.Count;
	public int TargetResolvedCount => Units.Count(unit => unit.TargetArchive is not null);
	public int GeometryEligibleCount => Units.Count(unit => unit.IsGeometryEligible);
	public int ExperimentalCandidateCount => Units.Sum(unit => unit.Adaptation?.Candidates.Count(candidate => candidate.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback) ?? 0);
}

public sealed record SameKeyUnitReconstructionPlan(
	AssetKey UnitAssetKey,
	PatchTocEntry SourceEntry,
	ArchiveMetadata? TargetArchive,
	IReadOnlyList<ArchiveMetadata> MatchingArchives,
	UnitMeshAdaptationPlan? Adaptation,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool IsSharedTarget => MatchingArchives.Count > 1;
	public bool HasBlockingIssue => Issues.Any(issue => issue.Severity == CoreIssueSeverity.Error);
	public bool HasExperimentalCandidate => Adaptation?.Candidates.Any(candidate => candidate.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback) == true;
	public bool IsGeometryEligible => Adaptation is { CanWrite: true, ReplacementCount: > 0 } && !HasBlockingIssue;
}