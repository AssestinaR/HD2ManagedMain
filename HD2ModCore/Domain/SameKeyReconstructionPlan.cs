namespace HD2ModCore.Domain;

// Purpose: Carries the plan for rebuilding each source Unit against its readable current same-key game-data target.
public sealed record SameKeyReconstructionRequest(
	string SourcePatchTocPath,
	string GameDataDirectory,
	bool AllowExperimentalCandidates = false,
	int? MaxSourceUnitCount = null)
{
	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(GameDataDirectory);
		if (MaxSourceUnitCount is <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSourceUnitCount), "Source Unit limit must be positive when specified.");
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
	public int ExperimentalCandidateCount => Units.Sum(unit => unit.Adaptation?.Steps.Count(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource && step.Candidate?.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback) ?? 0);
}

public sealed record SameKeyUnitReconstructionPlan(
	AssetKey UnitAssetKey,
	PatchTocEntry SourceEntry,
	ArchiveMetadata? TargetArchive,
	IReadOnlyList<ArchiveMetadata> MatchingArchives,
	UnitMeshAdaptationPlan? Adaptation,
	IReadOnlyList<CoreIssue> Issues,
	IReadOnlyList<SameKeyMeshEvidence>? MeshEvidence = null,
	int TargetMeshCount = 0,
	int CoveredTargetMeshCount = 0)
{
	public bool IsSharedTarget => MatchingArchives.Count > 1;
	public bool HasBlockingIssue => Issues.Any(issue => issue.Severity == CoreIssueSeverity.Error);
	public bool HasExperimentalCandidate => Adaptation?.Steps.Any(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource && step.Candidate?.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback) == true;
	public bool IsGeometryEligible => Adaptation is { CanWrite: true, ReplacementCount: > 0 } && !HasBlockingIssue && !HasExperimentalCandidate;
	public bool HasFullTargetShellCoverage => TargetMeshCount != 0 && CoveredTargetMeshCount == TargetMeshCount;
	public IReadOnlyList<SameKeyMeshEvidence> Evidence => MeshEvidence ?? Array.Empty<SameKeyMeshEvidence>();
}

// Purpose: Explains one source-to-current-target mesh candidate without retaining full GPU or vertex payloads in a plan report.
public sealed record SameKeyMeshEvidence(
	int SourceMeshInfoIndex,
	int TargetMeshInfoIndex,
	UnitMeshReplacementCandidateKind CandidateKind,
	int Score,
	string SourceSemanticName,
	string TargetSemanticName,
	int SourceLodIndex,
	int TargetLodIndex,
	int SourceVertexCount,
	int TargetVertexCount,
	int SourceTriangleCount,
	int TargetTriangleCount,
	int SourceSectionCount,
	int TargetSectionCount,
	uint SourceVertexStride,
	uint TargetVertexStride,
	IReadOnlyList<string> SourceComponents,
	IReadOnlyList<string> TargetComponents,
	IReadOnlyList<uint> SourceRealBoneIndices,
	IReadOnlyList<uint> TargetRealBoneIndices,
	int SharedRealBoneIndexCount,
	bool ReplacesTargetMesh,
	bool MinifiesTargetMesh,
	string Reason);