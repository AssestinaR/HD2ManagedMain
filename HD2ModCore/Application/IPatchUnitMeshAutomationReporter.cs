using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 patch-level Unit RawMesh 自动替换 dry-run 报告 API，用于汇总候选、编辑、跳过与失败原因。
// Purpose: Defines patch-level Unit RawMesh automatic replacement dry-run reporting APIs for summarizing candidates, edits, skips, and failures.
public interface IPatchUnitMeshAutomationReporter
{
	ValueTask<PatchUnitMeshAutomationReport> BuildReportAsync(
		IReadOnlyCollection<string> patchTocFilePaths,
		PatchTocEntry sourceEntry,
		int? sourceMeshInfoIndex = null,
		CancellationToken cancellationToken = default);
}
