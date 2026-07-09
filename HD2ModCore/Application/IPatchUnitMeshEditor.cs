using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 patch-level Unit mesh dry-run 编辑 API，生成待写回数据但不修改 patch 文件。
// Purpose: Defines patch-level Unit mesh dry-run editing APIs that produce rewritten payloads without modifying patch files.
public interface IPatchUnitMeshEditor
{
	ValueTask<PatchUnitMeshEditResult> MinifyAllAsync(PatchTocEntry entry, CancellationToken cancellationToken = default);

	ValueTask<PatchUnitMeshEditResult> MinifyRawMeshAsync(PatchTocEntry entry, int meshInfoIndex, CancellationToken cancellationToken = default);

	ValueTask<PatchUnitMeshEditResult> ReplaceRawMeshAsync(
		PatchTocEntry targetEntry,
		int targetMeshInfoIndex,
		PatchTocEntry sourceEntry,
		int sourceMeshInfoIndex,
		CancellationToken cancellationToken = default);
}
