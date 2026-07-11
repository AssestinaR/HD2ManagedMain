using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchArchiveBatchPlanner 能按 entry 汇总编辑、跳过、失败并生成 archive write plan。
// Purpose: Verifies PatchArchiveBatchPlanner summarizes edited, skipped, and failed entries and builds archive write plans.
public sealed class PatchArchiveBatchPlannerTests
{
	[Fact]
	public async Task BuildBatchPlanAsync_MixedEntries_RecordsStatusesAndBuildsWritePlan()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var entries = new[]
		{
			CreateEntry(patchPath, 0, 0x1111),
			CreateEntry(patchPath, 1, 0x2222),
			CreateEntry(patchPath, 2, 0x3333),
		};
		var scanner = new FakePatchTocScanner(entries);
		var dryWriter = new FakePatchArchiveDryWriter();
		var planner = new PatchArchiveBatchPlanner(scanner, dryWriter);

		var plan = await planner.BuildBatchPlanAsync(
			new[] { patchPath },
			(entry, _) =>
			{
				if (entry.EntryIndex == 0)
				{
					return ValueTask.FromResult<PatchUnitMeshEditResult?>(CreateEdit(entry));
				}
				if (entry.EntryIndex == 1)
				{
					return ValueTask.FromResult<PatchUnitMeshEditResult?>(null);
				}

				throw new InvalidDataException("Synthetic unsupported entry.");
			});

		Assert.Equal(1, plan.PatchCount);
		Assert.Equal(3, plan.EntryCount);
		Assert.Equal(1, plan.EditedEntryCount);
		Assert.Equal(1, plan.SkippedEntryCount);
		Assert.Equal(1, plan.FailedEntryCount);
		Assert.Single(plan.PatchPlans);
		Assert.Single(plan.PatchPlans[0].Edits);
		Assert.Equal(patchPath, dryWriter.LastPatchPath);
		Assert.Single(dryWriter.LastEdits!);
		Assert.Equal(PatchArchiveBatchEntryStatus.Edited, plan.EntryResults[0].Status);
		Assert.Equal(PatchArchiveBatchEntryStatus.Skipped, plan.EntryResults[1].Status);
		Assert.Equal(PatchArchiveBatchEntryStatus.Failed, plan.EntryResults[2].Status);
		Assert.Contains("Synthetic unsupported entry", plan.EntryResults[2].Reason, StringComparison.Ordinal);
	}

	[Fact]
	public async Task BuildBatchPlanAsync_AdditionalEntryFactory_PassesEntriesToWriter()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var entry = CreateEntry(patchPath, 0, 0x1111);
		var additionalKey = new AssetKey(0xeac0b497876adedf, 0x2222);
		var additionalEntry = new PatchArchiveAdditionalEntry(additionalKey, new byte[] { 7 }, Array.Empty<byte>(), Array.Empty<byte>());
		var scanner = new FakePatchTocScanner(new[] { entry });
		var dryWriter = new FakePatchArchiveDryWriter();
		var planner = new PatchArchiveBatchPlanner(scanner, dryWriter);

		var plan = await planner.BuildBatchPlanAsync(
			new[] { patchPath },
			(scannedEntry, _) => ValueTask.FromResult<PatchUnitMeshEditResult?>(CreateEdit(scannedEntry)),
			(path, edits, _) =>
			{
				Assert.Equal(patchPath, path);
				Assert.Single(edits);
				return ValueTask.FromResult<IReadOnlyCollection<PatchArchiveAdditionalEntry>>(new[] { additionalEntry });
			});

		Assert.Single(plan.PatchPlans);
		Assert.Equal(patchPath, dryWriter.LastPatchPath);
		Assert.Single(dryWriter.LastEdits!);
		Assert.Equal([additionalEntry], dryWriter.LastAdditionalEntries);
	}

	[Fact]
	public async Task BuildBatchPlanAsync_MismatchedEditIdentity_RecordsFailureAndDoesNotPassEditToWriter()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var entry = CreateEntry(patchPath, 0, 0x1111);
		var scanner = new FakePatchTocScanner(new[] { entry });
		var dryWriter = new FakePatchArchiveDryWriter();
		var planner = new PatchArchiveBatchPlanner(scanner, dryWriter);

		var plan = await planner.BuildBatchPlanAsync(
			new[] { patchPath },
			(scannedEntry, _) => ValueTask.FromResult<PatchUnitMeshEditResult?>(CreateEdit(scannedEntry with { EntryIndex = 99 })));

		Assert.Equal(1, plan.FailedEntryCount);
		Assert.Empty(plan.PatchPlans[0].Edits);
		Assert.Empty(dryWriter.LastEdits!);
		Assert.Contains("identity", plan.EntryResults[0].Reason, StringComparison.OrdinalIgnoreCase);
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
		=> new(
			entry,
			new PatchEntryPayload(entry, new byte[] { 1 }, Array.Empty<byte>(), Array.Empty<byte>()),
			CreateEmptyModel(),
			CreateEmptyModel(),
			new byte[] { 2 },
			Array.Empty<byte>());

	private static UnitMeshModel CreateEmptyModel()
		=> new(
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

	private sealed class FakePatchTocScanner : IPatchTocScanner
	{
		private readonly IReadOnlyList<PatchTocEntry> entries;

		public FakePatchTocScanner(IReadOnlyList<PatchTocEntry> entries)
			=> this.entries = entries;

		public ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlySet<AssetKey>>(entries.Select(e => e.AssetKey).ToHashSet());

		public IReadOnlySet<AssetKey> ScanAssetKeys(ReadOnlySpan<byte> tocData, bool usesSlimEntryOffset = false)
			=> entries.Select(e => e.AssetKey).ToHashSet();

		public IReadOnlyList<PatchTocEntry> ScanEntries(ReadOnlySpan<byte> tocData, string sourceFilePath, bool usesSlimEntryOffset = false)
			=> entries;

		public ValueTask<IReadOnlyList<PatchTocEntry>> ScanEntriesAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(entries);
	}

	private sealed class FakePatchArchiveDryWriter : IPatchArchiveDryWriter
	{
		public string? LastPatchPath { get; private set; }

		public IReadOnlyCollection<PatchUnitMeshEditResult>? LastEdits { get; private set; }

		public IReadOnlyCollection<PatchArchiveAdditionalEntry>? LastAdditionalEntries { get; private set; }

		public ValueTask<PatchArchiveWritePlan> BuildWritePlanAsync(
			string patchTocFilePath,
			IReadOnlyCollection<PatchUnitMeshEditResult> unitMeshEdits,
			IReadOnlyCollection<PatchTocEntry>? removedEntries = null,
			IReadOnlyCollection<PatchArchiveAdditionalEntry>? additionalEntries = null,
			CancellationToken cancellationToken = default)
		{
			LastPatchPath = patchTocFilePath;
			LastEdits = unitMeshEdits.ToArray();
			LastAdditionalEntries = additionalEntries?.ToArray() ?? Array.Empty<PatchArchiveAdditionalEntry>();
			var entries = unitMeshEdits.Select(e => e.Entry).ToArray();
			return ValueTask.FromResult(new PatchArchiveWritePlan(
				patchTocFilePath,
				new byte[] { 1, 2, 3 },
				Array.Empty<byte>(),
				Array.Empty<byte>(),
				entries,
				Array.Empty<PatchArchiveEditPlacement>()));
		}
	}
}
