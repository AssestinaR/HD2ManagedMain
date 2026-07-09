using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 patch-level Unit RawMesh 自动替换 dry-run 规划 API，输出候选与批量计划但不写文件。
// Purpose: Defines patch-level Unit RawMesh automatic replacement dry-run planning APIs that produce candidates and batch plans without writing files.
public interface IPatchUnitMeshReplacementPlanner
{
	ValueTask<PatchUnitMeshReplacementPlan> BuildReplacementPlanAsync(
		IReadOnlyCollection<string> patchTocFilePaths,
		PatchTocEntry sourceEntry,
		int? sourceMeshInfoIndex = null,
		CancellationToken cancellationToken = default);
}
