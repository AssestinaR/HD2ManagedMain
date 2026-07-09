namespace HD2ModCore.Domain;

// 作用：描述一个 patch archive 批量 dry-run 计划，包含每个 patch 的 write plan 与 entry 级处理结果。
// Purpose: Describes a batch dry-run plan for patch archives, including per-patch write plans and per-entry processing results.
public sealed record PatchArchiveBatchPlan(
	IReadOnlyList<PatchArchiveBatchPatchPlan> PatchPlans,
	IReadOnlyList<PatchArchiveBatchEntryResult> EntryResults)
{
	public int PatchCount => PatchPlans.Count;

	public int EntryCount => EntryResults.Count;

	public int EditedEntryCount => EntryResults.Count(e => e.Status == PatchArchiveBatchEntryStatus.Edited);

	public int SkippedEntryCount => EntryResults.Count(e => e.Status == PatchArchiveBatchEntryStatus.Skipped);

	public int FailedEntryCount => EntryResults.Count(e => e.Status == PatchArchiveBatchEntryStatus.Failed);
}
