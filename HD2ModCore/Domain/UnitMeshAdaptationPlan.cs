namespace HD2ModCore.Domain;

// 作用：保存 source Unit 到原版 target Unit 模板适配 dry-run 的报告、候选和执行步骤。
// Purpose: Holds the report, candidates, and execution steps for a source-to-target Unit adaptation dry-run.
public sealed record UnitMeshAdaptationPlan(
	UnitMeshAdaptationIntent Intent,
	bool CanWrite,
	IReadOnlyList<UnitMeshReplacementCandidate> Candidates,
	IReadOnlyList<UnitMeshAdaptationStep> Steps,
	string Reason)
{
	public int CandidateCount => Candidates.Count;

	public int ReplacementCount => Steps.Count(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource);

	public int MinifiedCount => Steps.Count(step => step.Kind == UnitMeshAdaptationStepKind.MinifyTarget);
}
