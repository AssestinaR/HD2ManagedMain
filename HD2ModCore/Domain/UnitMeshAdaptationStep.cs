namespace HD2ModCore.Domain;

// 作用：描述自动适配 dry-run 中对 target Unit 的一个 mesh slot 操作。
// Purpose: Describes one mesh slot operation applied to a target Unit during adaptation dry-runs.
public sealed record UnitMeshAdaptationStep(
	UnitMeshAdaptationStepKind Kind,
	int TargetMeshInfoIndex,
	int? SourceMeshInfoIndex,
	string Reason,
	UnitMeshReplacementCandidate? Candidate = null);
