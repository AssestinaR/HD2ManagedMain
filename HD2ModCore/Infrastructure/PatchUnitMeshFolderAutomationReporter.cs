using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从 patch 文件夹收集 TOC 文件并委托自动化报告器生成 Unit RawMesh 替换 dry-run 报告。
// Purpose: Collects patch TOC files from a directory and delegates Unit RawMesh replacement dry-run report generation.
public sealed class PatchUnitMeshFolderAutomationReporter : IPatchUnitMeshFolderAutomationReporter
{
	private readonly IPatchTocFileCollector patchTocFileCollector;
	private readonly IPatchUnitMeshAutomationReporter automationReporter;

	public PatchUnitMeshFolderAutomationReporter(
		IPatchTocFileCollector patchTocFileCollector,
		IPatchUnitMeshAutomationReporter automationReporter)
	{
		this.patchTocFileCollector = patchTocFileCollector ?? throw new ArgumentNullException(nameof(patchTocFileCollector));
		this.automationReporter = automationReporter ?? throw new ArgumentNullException(nameof(automationReporter));
	}

	public async ValueTask<PatchUnitMeshAutomationReport> BuildReportAsync(
		string patchDirectoryPath,
		PatchTocEntry sourceEntry,
		int? sourceMeshInfoIndex = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourceEntry);
		var fileSet = patchTocFileCollector.Collect(patchDirectoryPath);
		if (fileSet.Count == 0)
		{
			throw new InvalidDataException($"Patch directory does not contain any .patch_number TOC files: {fileSet.RootDirectoryPath}");
		}

		return await automationReporter.BuildReportAsync(
			fileSet.PatchTocFilePaths,
			sourceEntry,
			sourceMeshInfoIndex,
			cancellationToken).ConfigureAwait(false);
	}
}
