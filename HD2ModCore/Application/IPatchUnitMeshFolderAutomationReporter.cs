using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义从 patch 文件夹构建 Unit RawMesh 自动替换 dry-run 报告的入口 API。
// Purpose: Defines an entry API for building Unit RawMesh automatic replacement dry-run reports from a patch directory.
public interface IPatchUnitMeshFolderAutomationReporter
{
	ValueTask<PatchUnitMeshAutomationReport> BuildReportAsync(
		string patchDirectoryPath,
		PatchTocEntry sourceEntry,
		int? sourceMeshInfoIndex = null,
		CancellationToken cancellationToken = default);
}
