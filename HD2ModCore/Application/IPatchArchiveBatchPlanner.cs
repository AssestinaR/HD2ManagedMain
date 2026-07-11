using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 patch archive 批量 dry-run 计划 API，按 entry 记录编辑、跳过或失败原因。
// Purpose: Defines batch dry-run planning APIs for patch archives, recording edited, skipped, or failed results per entry.
public interface IPatchArchiveBatchPlanner
{
	ValueTask<PatchArchiveBatchPlan> BuildBatchPlanAsync(
		IReadOnlyCollection<string> patchTocFilePaths,
		Func<PatchTocEntry, CancellationToken, ValueTask<PatchUnitMeshEditResult?>> editFactory,
		Func<string, IReadOnlyCollection<PatchUnitMeshEditResult>, CancellationToken, ValueTask<IReadOnlyCollection<PatchArchiveAdditionalEntry>>>? additionalEntryFactory = null,
		CancellationToken cancellationToken = default);
}
