namespace HD2ModCore.Domain;

// 作用：描述一个 patch archive 在批量 dry-run 计划中的重建结果。
// Purpose: Describes one patch archive rebuild result within a batch dry-run plan.
public sealed record PatchArchiveBatchPatchPlan(
	string PatchTocFilePath,
	PatchArchiveWritePlan WritePlan,
	IReadOnlyList<PatchUnitMeshEditResult> Edits);
