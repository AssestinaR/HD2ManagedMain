namespace HD2ModCore.Domain;

// 作用：标识批量 patch archive dry-run 中单个 entry 的处理状态。
// Purpose: Identifies the processing status for one entry in a batch patch archive dry-run.
public enum PatchArchiveBatchEntryStatus
{
	Skipped = 0,
	Edited = 1,
	Failed = 2,
}
