using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies explicit source meshes are written as additions for the current target Unit identity.
public sealed class TargetShellUnitOutputBuilderTests
{
	private static readonly AssetKey SourceUnitKey = new(PatchUnitMeshReader.UnitTypeId, 0x1111);
	private static readonly AssetKey TargetUnitKey = new(PatchUnitMeshReader.UnitTypeId, 0x2222);

	[Fact]
	public void Build_WritesTargetUnitAdditionWithSourceMaterials()
	{
		var source = CreatePatchUnit(SourceUnitKey, CreateModel(10, 0x100));
		var targetPayload = new PatchEntryPayload(CreateEntry(TargetUnitKey), CreateWritableTocData(), Array.Empty<byte>(), Array.Empty<byte>());
		var target = new GameDataUnitMesh(TargetUnitKey, "target", targetPayload, CreateModel(20, 0x200), null);
		var mapping = new TargetShellMeshMapping(SourceUnitKey, 0, 0);

		var result = new TargetShellUnitOutputBuilder().Build(target, new[] { source }, new[] { mapping }, TargetShellDependencyPolicy.ReferenceCurrentGame);

		var output = Assert.Single(result.AdditionalEntries);
		Assert.Equal(TargetUnitKey, output.AssetKey);
		Assert.Equal(new[] { SourceUnitKey }, result.ReplacedSourceUnitAssetKeys);
		Assert.NotEmpty(output.TocData);
		Assert.NotEmpty(output.GpuResourceData);
	}

	[Fact]
	public void Build_RejectsMultipleMappingsForOneTargetMesh()
	{
		var source = CreatePatchUnit(SourceUnitKey, CreateModel(10, 0x100));
		var targetPayload = new PatchEntryPayload(CreateEntry(TargetUnitKey), CreateWritableTocData(), Array.Empty<byte>(), Array.Empty<byte>());
		var target = new GameDataUnitMesh(TargetUnitKey, "target", targetPayload, CreateModel(20, 0x200), null);
		var mapping = new TargetShellMeshMapping(SourceUnitKey, 0, 0);

		Assert.Throws<InvalidDataException>(() => new TargetShellUnitOutputBuilder().Build(target, new[] { source }, new[] { mapping, mapping }, TargetShellDependencyPolicy.ReferenceCurrentGame));
	}

	private static PatchUnitMesh CreatePatchUnit(AssetKey key, UnitMeshModel model)
	{
		var payload = new PatchEntryPayload(CreateEntry(key), CreateWritableTocData(), Array.Empty<byte>(), Array.Empty<byte>());
		return new PatchUnitMesh(payload.Entry, payload, model, null);
	}

	private static PatchTocEntry CreateEntry(AssetKey key) => new(key, "source.patch", "source.patch");

	private static UnitMeshModel CreateModel(uint materialSlot, ulong materialId)
	{
		var component = new UnitStreamComponentInfo(0, "position", 0, "vec3_float", 0, 0, 12);
		var stream = new UnitStreamInfo(0, 128, 0, 1, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0, new[] { component });
		var sectionInfo = new UnitMeshSectionInfo(300, 0, materialSlot, 0, 3, 0, 3, 0);
		var meshInfo = new UnitMeshInfo(0, 500, 1, 0, 0, 0, 1, 0, 1, 650, UnitMeshSemanticInfo.Empty(0, 0), new[] { materialSlot }, new[] { sectionInfo });
		var vertices = Enumerable.Range(0, 3)
			.Select(index => new UnitRawVertexRecord((uint)index, new byte[12], Array.Empty<UnitVertexComponentValue>()))
			.ToArray();
		var section = new UnitRawMeshSectionData(0, materialSlot, new[] { new UnitTriangleIndices(0, 1, 2) });
		var rawMesh = new UnitRawMeshData(0, 1, 0, 0, new[] { section }, section.Triangles, vertices);
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 496, 800, 900, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), new[] { stream }, new[] { meshInfo }, new[] { new UnitMaterialBinding(materialSlot, materialId) }, Array.Empty<UnitRawMeshSummary>(), new[] { rawMesh });
	}

	private static byte[] CreateWritableTocData()
	{
		var data = new byte[1200];
		WriteUInt32(data, 0x60, 900);
		WriteUInt32(data, 0x70, 800);
		WriteUInt32(data, 496, 4);
		WriteUInt32(data, 604, 1);
		WriteUInt32(data, 620, 1);
		WriteUInt32(data, 650, 0);
		WriteUInt32(data, 654, 0);
		WriteUInt32(data, 658, 3);
		WriteUInt32(data, 662, 0);
		WriteUInt32(data, 666, 3);
		WriteUInt32(data, 800, 1);
		WriteUInt32(data, 804, 20);
		return data;
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}
}