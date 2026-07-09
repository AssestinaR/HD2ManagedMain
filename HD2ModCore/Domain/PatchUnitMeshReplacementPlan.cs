namespace HD2ModCore.Domain;

// 作用：描述 patch-level Unit RawMesh 自动替换 dry-run 规划结果。
// Purpose: Describes a patch-level Unit RawMesh automatic replacement dry-run planning result.
public sealed record PatchUnitMeshReplacementPlan(
	PatchTocEntry SourceEntry,
	int? RequestedSourceMeshInfoIndex,
	IReadOnlyList<PatchUnitMeshReplacementCandidate> Candidates,
	PatchArchiveBatchPlan BatchPlan)
{
	public int CandidateCount => Candidates.Count;
}
