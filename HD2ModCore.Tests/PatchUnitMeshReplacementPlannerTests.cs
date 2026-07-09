using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchUnitMeshReplacementPlanner 能把结构匹配策略接入 patch-level 批量 dry-run 编辑流程。
// Purpose: Verifies PatchUnitMeshReplacementPlanner connects structural matching strategy to patch-level batch dry-run editing.
public sealed class PatchUnitMeshReplacementPlannerTests
{
	[Fact]
	public async Task BuildReplacementPlanAsync_CompatibleEntry_ProducesCandidateAndEdit()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var sourceEntry = CreateEntry(patchPath, 0, 0x1000);
		var compatibleTargetEntry = CreateEntry(patchPath, 1, 0x2000);
		var incompatibleTargetEntry = CreateEntry(patchPath, 2, 0x3000);
		var entries = new[] { sourceEntry, compatibleTargetEntry, incompatibleTargetEntry };
		var reader = new FakePatchUnitMeshReader(new Dictionary<PatchTocEntry, UnitMeshModel>
		{
			[sourceEntry] = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10]),
			[compatibleTargetEntry] = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10]),
			[incompatibleTargetEntry] = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10], componentFormat: 2),
		});
		var editor = new FakePatchUnitMeshEditor(reader);
		var planner = CreatePlanner(entries, reader, editor);

		var plan = await planner.BuildReplacementPlanAsync(new[] { patchPath }, sourceEntry);

		var candidate = Assert.Single(plan.Candidates);
		Assert.Equal(compatibleTargetEntry, candidate.TargetEntry);
		Assert.Equal(sourceEntry, candidate.SourceEntry);
		Assert.Equal(UnitMeshReplacementCandidateKind.SameMeshId, candidate.MeshCandidate.Kind);
		Assert.Equal(1, plan.BatchPlan.EditedEntryCount);
		Assert.Equal(2, plan.BatchPlan.SkippedEntryCount);
		Assert.Equal(0, plan.BatchPlan.FailedEntryCount);
		Assert.Single(editor.ReplaceCalls);
		Assert.Equal(compatibleTargetEntry, editor.ReplaceCalls[0].TargetEntry);
		Assert.Equal(sourceEntry, editor.ReplaceCalls[0].SourceEntry);
	}

	[Fact]
	public async Task BuildReplacementPlanAsync_SourceMeshFilter_UsesRequestedSourceMesh()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var sourceEntry = CreateEntry(patchPath, 0, 0x1000);
		var targetEntry = CreateEntry(patchPath, 1, 0x2000);
		var sourceModel = CreateModel(
			CreateRawMesh(meshInfoIndex: 0, meshId: 300, lodIndex: 5, materialSlots: [30]),
			CreateRawMesh(meshInfoIndex: 1, meshId: 200, lodIndex: 0, materialSlots: [10]));
		var targetModel = CreateModel(meshId: 200, lodIndex: 0, materialSlots: [10]);
		var reader = new FakePatchUnitMeshReader(new Dictionary<PatchTocEntry, UnitMeshModel>
		{
			[sourceEntry] = sourceModel,
			[targetEntry] = targetModel,
		});
		var editor = new FakePatchUnitMeshEditor(reader);
		var planner = CreatePlanner(new[] { sourceEntry, targetEntry }, reader, editor);

		var plan = await planner.BuildReplacementPlanAsync(new[] { patchPath }, sourceEntry, sourceMeshInfoIndex: 1);

		var candidate = Assert.Single(plan.Candidates);
		Assert.Equal(1, candidate.MeshCandidate.SourceMeshInfoIndex);
		Assert.Equal(UnitMeshReplacementCandidateKind.SameMeshId, candidate.MeshCandidate.Kind);
		Assert.Single(editor.ReplaceCalls);
		Assert.Equal(1, editor.ReplaceCalls[0].SourceMeshInfoIndex);
	}

	[Fact]
	public async Task BuildReplacementPlanAsync_MissingRequestedSourceMesh_Throws()
	{
		var patchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".patch_0");
		var sourceEntry = CreateEntry(patchPath, 0, 0x1000);
		var reader = new FakePatchUnitMeshReader(new Dictionary<PatchTocEntry, UnitMeshModel>
		{
			[sourceEntry] = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10]),
		});
		var editor = new FakePatchUnitMeshEditor(reader);
		var planner = CreatePlanner(new[] { sourceEntry }, reader, editor);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
			await planner.BuildReplacementPlanAsync(new[] { patchPath }, sourceEntry, sourceMeshInfoIndex: 99));
	}

	private static PatchUnitMeshReplacementPlanner CreatePlanner(
		IReadOnlyList<PatchTocEntry> entries,
		IPatchUnitMeshReader reader,
		IPatchUnitMeshEditor editor)
	{
		var scanner = new FakePatchTocScanner(entries);
		var dryWriter = new FakePatchArchiveDryWriter();
		var batchPlanner = new PatchArchiveBatchPlanner(scanner, dryWriter);
		return new PatchUnitMeshReplacementPlanner(batchPlanner, reader, editor, new UnitMeshReplacementStrategy());
	}

	private static PatchTocEntry CreateEntry(string patchPath, uint entryIndex, ulong fileId)
		=> new(
			new AssetKey(0xe0a48d0be9a7453f, fileId),
			patchPath,
			Path.GetFileName(patchPath),
			TocDataOffset: entryIndex * 10,
			TocDataSize: 1,
			EntryIndex: entryIndex);

	private static PatchUnitMesh CreateUnit(PatchTocEntry entry, UnitMeshModel model)
		=> new(entry, new PatchEntryPayload(entry, new byte[] { 1 }, Array.Empty<byte>(), Array.Empty<byte>()), model);

	private static PatchUnitMeshEditResult CreateEdit(PatchTocEntry entry, UnitMeshModel model)
		=> new(
			entry,
			new PatchEntryPayload(entry, new byte[] { 1 }, Array.Empty<byte>(), Array.Empty<byte>()),
			model,
			model,
			new byte[] { 2 },
			Array.Empty<byte>());

	private static UnitMeshModel CreateModel(uint meshId, int lodIndex, uint[] materialSlots, uint componentFormat = 1)
	{
		var rawMesh = CreateRawMesh(0, meshId, lodIndex, materialSlots);
		return CreateModel([rawMesh], componentFormat);
	}

	private static UnitMeshModel CreateModel(params UnitRawMeshData[] rawMeshes)
		=> CreateModel(rawMeshes, componentFormat: 1);

	private static UnitMeshModel CreateModel(UnitRawMeshData[] rawMeshes, uint componentFormat)
	{
		var streamIndexes = rawMeshes.Select(mesh => mesh.StreamIndex).Distinct().ToArray();
		var streams = streamIndexes.Select(index => CreateStream((int)index, componentFormat)).ToArray();
		return new UnitMeshModel(
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
			streams,
			Array.Empty<UnitMeshInfo>(),
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			rawMeshes);
	}

	private static UnitStreamInfo CreateStream(int index, uint componentFormat)
		=> new(
			index,
			0,
			0,
			1,
			0,
			3,
			12,
			0,
			3,
			0,
			0,
			36,
			36,
			6,
			[new UnitStreamComponentInfo(1, "POSITION", componentFormat, "Float3", 0, 0, 12)]);

	private static UnitRawMeshData CreateRawMesh(int meshInfoIndex, uint meshId, int lodIndex, uint[] materialSlots)
	{
		var sections = materialSlots.Select(slot => new UnitRawMeshSectionData(0, slot, [new UnitTriangleIndices(0, 1, 2)])).ToArray();
		return new UnitRawMeshData(
			meshInfoIndex,
			meshId,
			lodIndex,
			0,
			sections,
			sections.SelectMany(section => section.Triangles).ToArray(),
			[
				new UnitRawVertexRecord(0, new byte[12], Array.Empty<UnitVertexComponentValue>()),
				new UnitRawVertexRecord(1, new byte[12], Array.Empty<UnitVertexComponentValue>()),
				new UnitRawVertexRecord(2, new byte[12], Array.Empty<UnitVertexComponentValue>()),
			]);
	}

	private sealed class FakePatchUnitMeshReader : IPatchUnitMeshReader
	{
		private readonly IReadOnlyDictionary<PatchTocEntry, UnitMeshModel> models;

		public FakePatchUnitMeshReader(IReadOnlyDictionary<PatchTocEntry, UnitMeshModel> models)
			=> this.models = models;

		public ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(CreateUnit(entry, models[entry]));
	}

	private sealed class FakePatchUnitMeshEditor : IPatchUnitMeshEditor
	{
		private readonly FakePatchUnitMeshReader reader;

		public FakePatchUnitMeshEditor(FakePatchUnitMeshReader reader)
			=> this.reader = reader;

		public List<ReplaceCall> ReplaceCalls { get; } = [];

		public ValueTask<PatchUnitMeshEditResult> MinifyAllAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<PatchUnitMeshEditResult> MinifyRawMeshAsync(PatchTocEntry entry, int meshInfoIndex, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public async ValueTask<PatchUnitMeshEditResult> ReplaceRawMeshAsync(
			PatchTocEntry targetEntry,
			int targetMeshInfoIndex,
			PatchTocEntry sourceEntry,
			int sourceMeshInfoIndex,
			CancellationToken cancellationToken = default)
		{
			ReplaceCalls.Add(new ReplaceCall(targetEntry, targetMeshInfoIndex, sourceEntry, sourceMeshInfoIndex));
			var unit = await reader.ReadUnitMeshAsync(targetEntry, cancellationToken);
			return CreateEdit(targetEntry, unit.Model);
		}
	}

	private sealed record ReplaceCall(PatchTocEntry TargetEntry, int TargetMeshInfoIndex, PatchTocEntry SourceEntry, int SourceMeshInfoIndex);

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
		public ValueTask<PatchArchiveWritePlan> BuildWritePlanAsync(
			string patchTocFilePath,
			IReadOnlyCollection<PatchUnitMeshEditResult> unitMeshEdits,
			IReadOnlyCollection<PatchTocEntry>? removedEntries = null,
			CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(new PatchArchiveWritePlan(
				patchTocFilePath,
				new byte[] { 1, 2, 3 },
				Array.Empty<byte>(),
				Array.Empty<byte>(),
				unitMeshEdits.Select(edit => edit.Entry).ToArray(),
				Array.Empty<PatchArchiveEditPlacement>()));
		}
	}
}
