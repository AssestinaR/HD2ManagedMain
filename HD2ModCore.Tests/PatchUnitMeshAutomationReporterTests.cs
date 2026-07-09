using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchUnitMeshAutomationReporter 能把 replacement plan 转换为 UI/CLI 可消费的 dry-run 报告。
// Purpose: Verifies PatchUnitMeshAutomationReporter converts replacement plans into UI/CLI-friendly dry-run reports.
public sealed class PatchUnitMeshAutomationReporterTests
{
	[Fact]
	public async Task BuildReportAsync_ReplacementPlan_SummarizesEntriesAndCandidates()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var sourceEntry = CreateEntry(patchPath, 0, 0x1000);
		var editedEntry = CreateEntry(patchPath, 1, 0x2000);
		var skippedEntry = CreateEntry(patchPath, 2, 0x3000);
		var failedEntry = CreateEntry(patchPath, 3, 0x4000);
		var candidate = new PatchUnitMeshReplacementCandidate(
			editedEntry,
			sourceEntry,
			new UnitMeshReplacementCandidate(
				TargetMeshInfoIndex: 0,
				SourceMeshInfoIndex: 1,
				TargetMeshId: 0x2000,
				SourceMeshId: 0x1000,
				TargetSemanticName: string.Empty,
				SourceSemanticName: string.Empty,
				LodIndex: 0,
				StreamIndex: 0,
				VertexStride: 12,
				ComponentLayout: [new UnitMeshReplacementComponentSignature(1, 1, 0, 12)],
				Kind: UnitMeshReplacementCandidateKind.SameLod,
				Score: 20,
				Reason: "same lod"));
		var exception = new InvalidDataException("bad unit");
		var edit = CreateEdit(editedEntry);
		var plan = new PatchUnitMeshReplacementPlan(
			sourceEntry,
			RequestedSourceMeshInfoIndex: 1,
			[candidate],
			new PatchArchiveBatchPlan(
				[
					new PatchArchiveBatchPatchPlan(
						patchPath,
						new PatchArchiveWritePlan(patchPath, [1], [], [], [editedEntry], []),
						[edit]),
				],
				[
					new PatchArchiveBatchEntryResult(editedEntry, PatchArchiveBatchEntryStatus.Edited, "Edit produced.", edit),
					new PatchArchiveBatchEntryResult(skippedEntry, PatchArchiveBatchEntryStatus.Skipped, "No edit produced."),
					new PatchArchiveBatchEntryResult(failedEntry, PatchArchiveBatchEntryStatus.Failed, "bad unit", Exception: exception),
				]));
		var reporter = new PatchUnitMeshAutomationReporter(new FakePatchUnitMeshReplacementPlanner(plan));

		var report = await reporter.BuildReportAsync([patchPath], sourceEntry, sourceMeshInfoIndex: 1);

		Assert.Equal(sourceEntry, report.SourceEntry);
		Assert.Equal(1, report.RequestedSourceMeshInfoIndex);
		Assert.Equal(3, report.EntryCount);
		Assert.Equal(1, report.CandidateCount);
		Assert.Equal(1, report.EditedEntryCount);
		Assert.Equal(1, report.SkippedEntryCount);
		Assert.Equal(1, report.FailedEntryCount);
		var editedReport = Assert.Single(report.EntryReports, entry => entry.Status == PatchArchiveBatchEntryStatus.Edited);
		Assert.True(editedReport.HasCandidate);
		Assert.Same(candidate, editedReport.Candidate);
		Assert.Contains("SameLod", editedReport.Reason, StringComparison.Ordinal);
		Assert.Contains("same lod", editedReport.Reason, StringComparison.Ordinal);
		var failedReport = Assert.Single(report.EntryReports, entry => entry.Status == PatchArchiveBatchEntryStatus.Failed);
		Assert.Same(exception, failedReport.Exception);
	}

	[Fact]
	public async Task BuildReportAsync_ForwardsInputsToReplacementPlanner()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var sourceEntry = CreateEntry(patchPath, 0, 0x1000);
		var plan = new PatchUnitMeshReplacementPlan(
			sourceEntry,
			RequestedSourceMeshInfoIndex: null,
			Array.Empty<PatchUnitMeshReplacementCandidate>(),
			new PatchArchiveBatchPlan(Array.Empty<PatchArchiveBatchPatchPlan>(), Array.Empty<PatchArchiveBatchEntryResult>()));
		var replacementPlanner = new FakePatchUnitMeshReplacementPlanner(plan);
		var reporter = new PatchUnitMeshAutomationReporter(replacementPlanner);

		await reporter.BuildReportAsync([patchPath], sourceEntry, sourceMeshInfoIndex: 7);

		Assert.Equal(new[] { patchPath }, replacementPlanner.PatchTocFilePaths);
		Assert.Equal(sourceEntry, replacementPlanner.SourceEntry);
		Assert.Equal(7, replacementPlanner.SourceMeshInfoIndex);
	}

	private static PatchTocEntry CreateEntry(string patchPath, uint entryIndex, ulong fileId)
		=> new(
			new AssetKey(0xe0a48d0be9a7453f, fileId),
			patchPath,
			Path.GetFileName(patchPath),
			TocDataOffset: entryIndex * 10,
			TocDataSize: 1,
			EntryIndex: entryIndex);

	private static PatchUnitMeshEditResult CreateEdit(PatchTocEntry entry)
	{
		var model = new UnitMeshModel(
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			Array.Empty<UnitBoneInfo>(),
			Array.Empty<UnitStreamInfo>(),
			Array.Empty<UnitMeshInfo>(),
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			Array.Empty<UnitRawMeshData>());
		return new PatchUnitMeshEditResult(
			entry,
			new PatchEntryPayload(entry, [1], [], []),
			model,
			model,
			[2],
			[]);
	}

	private sealed class FakePatchUnitMeshReplacementPlanner : IPatchUnitMeshReplacementPlanner
	{
		private readonly PatchUnitMeshReplacementPlan plan;

		public FakePatchUnitMeshReplacementPlanner(PatchUnitMeshReplacementPlan plan)
		{
			this.plan = plan;
		}

		public IReadOnlyCollection<string>? PatchTocFilePaths { get; private set; }

		public PatchTocEntry? SourceEntry { get; private set; }

		public int? SourceMeshInfoIndex { get; private set; }

		public ValueTask<PatchUnitMeshReplacementPlan> BuildReplacementPlanAsync(
			IReadOnlyCollection<string> patchTocFilePaths,
			PatchTocEntry sourceEntry,
			int? sourceMeshInfoIndex = null,
			CancellationToken cancellationToken = default)
		{
			PatchTocFilePaths = patchTocFilePaths;
			SourceEntry = sourceEntry;
			SourceMeshInfoIndex = sourceMeshInfoIndex;
			return ValueTask.FromResult(plan);
		}
	}
}
