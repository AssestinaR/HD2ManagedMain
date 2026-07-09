using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 patch archive dry-run 重建 API，更新 entry offset/size 并生成新文件 bytes 但不落盘。
// Purpose: Defines patch archive dry-run rebuild APIs that update entry offsets/sizes and produce new file bytes without writing to disk.
public interface IPatchArchiveDryWriter
{
	ValueTask<PatchArchiveWritePlan> BuildWritePlanAsync(
		string patchTocFilePath,
		IReadOnlyCollection<PatchUnitMeshEditResult> unitMeshEdits,
		IReadOnlyCollection<PatchTocEntry>? removedEntries = null,
		CancellationToken cancellationToken = default);
}
