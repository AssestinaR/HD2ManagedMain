namespace HD2ModCore.Domain;

// 作用：描述批量 patch archive dry-run 中单个 entry 的处理结果。
// Purpose: Describes the processing result for one entry in a batch patch archive dry-run.
public sealed record PatchArchiveBatchEntryResult(
	PatchTocEntry Entry,
	PatchArchiveBatchEntryStatus Status,
	string Reason,
	PatchUnitMeshEditResult? Edit = null,
	Exception? Exception = null);
