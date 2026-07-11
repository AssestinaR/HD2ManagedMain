using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 patch Unit mesh 依赖收集只跟随实际替换 section 使用的材质。
// Purpose: Verifies patch Unit mesh dependency collection follows only materials used by replaced sections.
public sealed class PatchUnitMeshMaterialDependencyCollectorTests
{
	[Fact]
	public void CollectReplacementMaterialIds_UsesEditedRawSectionSlots()
	{
		var entry = CreateEntry();
		var editedModel = CreateModel(
			[
				new UnitMaterialBinding(100u, 0xAAAAAAAAAAAAAAAAul),
				new UnitMaterialBinding(200u, 0xBBBBBBBBBBBBBBBBul),
			],
			[
				CreateRawMesh(meshInfoIndex: 0, [100u]),
				CreateRawMesh(meshInfoIndex: 1, [200u]),
			]);
		var edit = CreateEdit(entry, editedModel) with
		{
			AdaptationSteps =
			[
				new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.ReplaceWithSource, 0, 0, "replace")
			]
		};

		var materialIds = PatchUnitMeshMaterialDependencyCollector.CollectReplacementMaterialIds([edit]);

		Assert.Equal([0xAAAAAAAAAAAAAAAAul], materialIds.OrderBy(id => id).ToArray());
	}

	[Fact]
	public void CollectReplacementMaterialIds_EditWithoutReplacementStep_IsIgnored()
	{
		var entry = CreateEntry();
		var editedModel = CreateModel(
			[new UnitMaterialBinding(100u, 0xAAAAAAAAAAAAAAAAul)],
			[CreateRawMesh(meshInfoIndex: 0, [100u])]);
		var edit = CreateEdit(entry, editedModel) with
		{
			AdaptationSteps =
			[
				new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.MinifyTarget, 0, null, "minify")
			]
		};

		var materialIds = PatchUnitMeshMaterialDependencyCollector.CollectReplacementMaterialIds([edit]);

		Assert.Empty(materialIds);
	}

	private static PatchTocEntry CreateEntry()
		=> new(
			new AssetKey(0xe0a48d0be9a7453f, 0x1111),
			Path.Combine(Path.GetTempPath(), "test.patch_0"),
			"test.patch_0",
			TocDataOffset: 0,
			TocDataSize: 1,
			EntryIndex: 0);

	private static PatchUnitMeshEditResult CreateEdit(PatchTocEntry entry, UnitMeshModel model)
		=> new(
			entry,
			new PatchEntryPayload(entry, new byte[] { 1 }, Array.Empty<byte>(), Array.Empty<byte>()),
			model,
			model,
			new byte[] { 2 },
			Array.Empty<byte>());

	private static UnitMeshModel CreateModel(IReadOnlyList<UnitMaterialBinding> materials, IReadOnlyList<UnitRawMeshData> rawMeshes)
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
			materials,
			Array.Empty<UnitRawMeshSummary>(),
			rawMeshes);

	private static UnitRawMeshData CreateRawMesh(int meshInfoIndex, IReadOnlyList<uint> materialSlots)
	{
		var sections = materialSlots.Select(slot => new UnitRawMeshSectionData(0, slot, [new UnitTriangleIndices(0, 1, 2)])).ToArray();
		return new UnitRawMeshData(
			meshInfoIndex,
			0,
			0,
			0,
			sections,
			sections.SelectMany(section => section.Triangles).ToArray(),
			Array.Empty<UnitRawVertexRecord>());
	}
}