using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证文件夹级自动化报告器会收集 patch TOC 并委托 dry-run 报告服务。
// Purpose: Verifies the folder-level automation reporter collects patch TOCs and delegates dry-run report generation.
public sealed class PatchUnitMeshFolderAutomationReporterTests
{
	[Fact]
	public async Task BuildReportAsync_DirectoryWithPatchFiles_ForwardsCollectedTocs()
	{
		var root = CreateTempDirectory();
		try
		{
			var patch0 = Path.Combine(root, "sample.patch_0");
			var patch1 = Path.Combine(root, "sample.patch_1");
			File.WriteAllBytes(patch0, []);
			File.WriteAllBytes(patch1, []);
			File.WriteAllBytes(Path.Combine(root, "sample.patch_1.gpu_resources"), []);
			var sourceEntry = CreateEntry(patch0);
			var report = CreateReport(sourceEntry);
			var automationReporter = new FakePatchUnitMeshAutomationReporter(report);
			var folderReporter = new PatchUnitMeshFolderAutomationReporter(new PatchTocFileCollector(), automationReporter);

			var result = await folderReporter.BuildReportAsync(root, sourceEntry, sourceMeshInfoIndex: 5);

			Assert.Same(report, result);
			Assert.Equal([patch0, patch1], automationReporter.PatchTocFilePaths);
			Assert.Equal(sourceEntry, automationReporter.SourceEntry);
			Assert.Equal(5, automationReporter.SourceMeshInfoIndex);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public async Task BuildReportAsync_DirectoryWithoutPatchFiles_Throws()
	{
		var root = CreateTempDirectory();
		try
		{
			File.WriteAllBytes(Path.Combine(root, "sample.patch_0.gpu_resources"), []);
			var sourceEntry = CreateEntry(Path.Combine(root, "sample.patch_0"));
			var reporter = new PatchUnitMeshFolderAutomationReporter(
				new PatchTocFileCollector(),
				new FakePatchUnitMeshAutomationReporter(CreateReport(sourceEntry)));

			await Assert.ThrowsAsync<InvalidDataException>(async () => await reporter.BuildReportAsync(root, sourceEntry));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static PatchTocEntry CreateEntry(string patchPath)
		=> new(
			new AssetKey(0xe0a48d0be9a7453f, 0x1000),
			patchPath,
			Path.GetFileName(patchPath),
			TocDataSize: 1);

	private static PatchUnitMeshAutomationReport CreateReport(PatchTocEntry sourceEntry)
	{
		var plan = new PatchUnitMeshReplacementPlan(
			sourceEntry,
			RequestedSourceMeshInfoIndex: null,
			Array.Empty<PatchUnitMeshReplacementCandidate>(),
			new PatchArchiveBatchPlan(Array.Empty<PatchArchiveBatchPatchPlan>(), Array.Empty<PatchArchiveBatchEntryResult>()));
		return new PatchUnitMeshAutomationReport(plan, Array.Empty<PatchUnitMeshAutomationEntryReport>());
	}

	private static string CreateTempDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private sealed class FakePatchUnitMeshAutomationReporter : IPatchUnitMeshAutomationReporter
	{
		private readonly PatchUnitMeshAutomationReport report;

		public FakePatchUnitMeshAutomationReporter(PatchUnitMeshAutomationReport report)
		{
			this.report = report;
		}

		public IReadOnlyCollection<string>? PatchTocFilePaths { get; private set; }

		public PatchTocEntry? SourceEntry { get; private set; }

		public int? SourceMeshInfoIndex { get; private set; }

		public ValueTask<PatchUnitMeshAutomationReport> BuildReportAsync(
			IReadOnlyCollection<string> patchTocFilePaths,
			PatchTocEntry sourceEntry,
			int? sourceMeshInfoIndex = null,
			CancellationToken cancellationToken = default)
		{
			PatchTocFilePaths = patchTocFilePaths;
			SourceEntry = sourceEntry;
			SourceMeshInfoIndex = sourceMeshInfoIndex;
			return ValueTask.FromResult(report);
		}
	}
}
