namespace HD2ModCore.Domain;

// 作用：记录一个被编辑 entry 在 dry-run 重建前后的 offset/size 变化。
// Purpose: Records offset/size changes for one edited entry in a dry-run archive rebuild.
public sealed record PatchArchiveEditPlacement(
	PatchTocEntry OriginalEntry,
	PatchTocEntry UpdatedEntry,
	int TocDataSizeDelta,
	int GpuResourceSizeDelta);
