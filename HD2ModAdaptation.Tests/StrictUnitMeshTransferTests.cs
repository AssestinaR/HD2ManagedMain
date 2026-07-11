using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies strict explicit Unit mesh transfer semantics with source material propagation.
public sealed class StrictUnitMeshTransferTests
{
	[Fact]
	public void Transfer_CopiesGeometrySourceMaterialsAndRemappedBones()
	{
		var source = CreateModel(materialSlot: 10, materialId: 0x100, realBoneIndices: new uint[] { 42 }, fakeBoneIndices: new uint[] { 0 }, boneValue: 0);
		var target = CreateModel(materialSlot: 20, materialId: 0x200, realBoneIndices: new uint[] { 99, 42 }, fakeBoneIndices: new uint[] { 0, 1 }, boneValue: 0);

		var result = new StrictUnitMeshTransfer().Transfer(target, 0, source, 0);

		var mesh = Assert.Single(result.Model.RawMeshData);
		Assert.Equal(new byte[] { 1, 1, 1, 1 }, mesh.Vertices[0].Data);
		Assert.Equal((uint)20, Assert.Single(mesh.Sections).MaterialSlotId);
		Assert.Equal((uint)0, Assert.Single(mesh.Sections).MaterialIndex);
		Assert.Equal((ulong)0x100, Assert.Single(result.Model.Materials).MaterialId);
		Assert.Equal(new ulong[] { 0x100 }, result.ReplacementMaterialIds);
	}

	[Fact]
	public void Transfer_RejectsDifferentVertexLayouts()
	{
		var source = CreateModel(10, 0x100, new uint[] { 42 }, new uint[] { 0 }, 0);
		var target = CreateModel(20, 0x200, new uint[] { 42 }, new uint[] { 0 }, 0, stride: 8);

		Assert.Throws<InvalidDataException>(() => new StrictUnitMeshTransfer().Transfer(target, 0, source, 0));
	}

	[Fact]
	public void Transfer_TargetLayoutConversion_ReencodesVerticesAndPropagatesSourceMaterials()
	{
		var source = CreateModel(10, 0x100, new uint[] { 42 }, new uint[] { 0 }, 0, components: new[]
		{
			new UnitStreamComponentInfo(4, "uv", 8, "vec4_1010102", 0, 0, 4)
		}, vertexFloatValues: new[] { 0.25f, 0.5f, 0f, 1f });
		var target = CreateModel(20, 0x200, new uint[] { 42 }, new uint[] { 0 }, 0, stride: 4, components: new[]
		{
			new UnitStreamComponentInfo(4, "uv", 6, "vec2_half", 0, 0, 4)
		});

		var result = new StrictUnitMeshTransfer(allowTargetLayoutConversion: true).Transfer(target, 0, source, 0);

		var vertex = Assert.Single(result.Model.RawMeshData).Vertices[0];
		Assert.Equal((uint)4, (uint)vertex.Data.Length);
		Assert.Equal((Half)0.25f, BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(vertex.Data, 0)));
		Assert.Equal((Half)0.5f, BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(vertex.Data, 2)));
		Assert.Equal((ulong)0x100, Assert.Single(result.Model.Materials).MaterialId);
		Assert.Equal(new ulong[] { 0x100 }, result.ReplacementMaterialIds);
	}

	[Fact]
	public void Transfer_MissingTargetBone_PreservesUnmappedSourceIndexLikeCore()
	{
		var source = CreateModel(materialSlot: 10, materialId: 0x100, realBoneIndices: new uint[] { 42, 77 }, fakeBoneIndices: new uint[] { 0, 1 }, boneValue: 1);
		var target = CreateModel(materialSlot: 20, materialId: 0x200, realBoneIndices: new uint[] { 42 }, fakeBoneIndices: new uint[] { 0 }, boneValue: 0);

		var result = new StrictUnitMeshTransfer().Transfer(target, 0, source, 0);

		var vertex = Assert.Single(result.Model.RawMeshData).Vertices[0];
		Assert.Equal((byte)1, vertex.Data[0]);
	}

	[Fact]
	public void Transfer_TargetLayoutConversion_PreservesUnmappedInfluenceLikeCore()
	{
		var source = CreateModel(materialSlot: 10, materialId: 0x100, realBoneIndices: new uint[] { 42, 77 }, fakeBoneIndices: new uint[] { 0, 1 }, boneValue: 0, stride: 4, components: new[]
		{
			new UnitStreamComponentInfo(6, "bone_indices", 0, "vec4_uint8", 0, 0, 4)
		}, vertexUIntValues: new uint[] { 0, 1, 0, 0 }, vertexFloatValues: new[] { 0.25f, 0.75f, 0f, 0f });
		var target = CreateModel(materialSlot: 20, materialId: 0x200, realBoneIndices: new uint[] { 42 }, fakeBoneIndices: new uint[] { 0 }, boneValue: 0, stride: 4, components: new[]
		{
			new UnitStreamComponentInfo(6, "bone_indices", 0, "vec4_uint8", 0, 0, 4)
		});

		var result = new StrictUnitMeshTransfer(allowTargetLayoutConversion: true).Transfer(target, 0, source, 0);

		var vertex = Assert.Single(result.Model.RawMeshData).Vertices[0];
		Assert.Equal(new byte[] { 0, 1, 0, 0 }, vertex.Data);
	}

	[Fact]
	public void Transfer_SourcePatchMaterials_ExpandsTargetMaterialSlotsAndSections()
	{
		var source = CreateUnskinnedModel(new uint[] { 10, 11 }, new ulong[] { 0x100, 0x101 });
		var target = CreateUnskinnedModel(new uint[] { 20 }, new ulong[] { 0x200 });

		var result = new StrictUnitMeshTransfer().Transfer(target, 0, source, 0);

		var meshInfo = Assert.Single(result.Model.Meshes);
		Assert.Equal(2, meshInfo.MaterialSlotIds.Count);
		Assert.Equal(2, meshInfo.Sections.Count);
		Assert.Equal(2, Assert.Single(result.Model.RawMeshData).Sections.Count);
		Assert.Equal(new ulong[] { 0x100, 0x101 }, result.ReplacementMaterialIds.OrderBy(id => id));
		Assert.Contains(result.Model.Materials, binding => binding.SectionId == meshInfo.MaterialSlotIds[0] && binding.MaterialId == 0x100);
		Assert.Contains(result.Model.Materials, binding => binding.SectionId == meshInfo.MaterialSlotIds[1] && binding.MaterialId == 0x101);
	}

	private static UnitMeshModel CreateModel(uint materialSlot, ulong materialId, IReadOnlyList<uint> realBoneIndices, IReadOnlyList<uint> fakeBoneIndices, byte boneValue, uint stride = 4, IReadOnlyList<UnitStreamComponentInfo>? components = null, float[]? vertexFloatValues = null, uint[]? vertexUIntValues = null)
	{
		components ??= new[] { new UnitStreamComponentInfo(6, "bone_indices", 0, "vec4_uint8", 0, 0, 4) };
		var stream = new UnitStreamInfo(0, 0, 0, 1, 0, 3, stride, 0, 3, 0, 0, 0, 0, 0, components);
		var sectionInfo = new UnitMeshSectionInfo(0, 0, materialSlot, 0, 3, 0, 3, 0);
		var meshInfo = new UnitMeshInfo(0, 0, 1, 0, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(0, 0), new[] { materialSlot }, new[] { sectionInfo });
		var remap = new UnitBoneRemap(0, 0, fakeBoneIndices);
		var boneInfo = new UnitBoneInfo(0, 0, (uint)realBoneIndices.Count, 0, 0, 0, realBoneIndices, new[] { remap });
		var firstComponent = components[0];
		var vertexData = new byte[checked((int)stride)];
		var uintValues = vertexUIntValues ?? new uint[] { boneValue, 0, 0, 0 };
		for (var index = 0; index < Math.Min(4, vertexData.Length); index++)
		{
			vertexData[index] = (byte)uintValues[index];
		}
		var vertices = Enumerable.Range(0, 3).Select(index => new UnitRawVertexRecord((uint)index, vertexData.ToArray(), new[] { new UnitVertexComponentValue(firstComponent.Type, firstComponent.TypeName, firstComponent.Format, firstComponent.FormatName, firstComponent.Index, vertexFloatValues ?? Array.Empty<float>(), uintValues, vertexData.ToArray()) })).ToArray();
		var section = new UnitRawMeshSectionData(0, materialSlot, new[] { new UnitTriangleIndices(0, 1, 2) });
		var rawMesh = new UnitRawMeshData(0, 1, 0, 0, new[] { section }, section.Triangles, vertices);
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, new[] { boneInfo }, new[] { stream }, new[] { meshInfo }, new[] { new UnitMaterialBinding(materialSlot, materialId) }, Array.Empty<UnitRawMeshSummary>(), new[] { rawMesh });
	}

	private static UnitMeshModel CreateUnskinnedModel(IReadOnlyList<uint> materialSlots, IReadOnlyList<ulong> materialIds)
	{
		var component = new UnitStreamComponentInfo(0, "position", 0, "vec3_float", 0, 0, 12);
		var stream = new UnitStreamInfo(0, 0, 0, 1, 0, 6, 12, 0, 6, 0, 0, 0, 0, 0, new[] { component });
		var sectionInfos = materialSlots.Select((slot, index) => new UnitMeshSectionInfo(0, (uint)index, slot, (uint)(index * 3), 3, (uint)(index * 3), 3, 0)).ToArray();
		var meshInfo = new UnitMeshInfo(0, 0, 1, 0, 0, 0, (uint)materialSlots.Count, 0, (uint)materialSlots.Count, 0, UnitMeshSemanticInfo.Empty(0, 0), materialSlots.ToArray(), sectionInfos);
		var vertices = Enumerable.Range(0, materialSlots.Count * 3)
			.Select(index => new UnitRawVertexRecord((uint)index, new byte[12], Array.Empty<UnitVertexComponentValue>()))
			.ToArray();
		var sections = materialSlots.Select((slot, index) => new UnitRawMeshSectionData((uint)index, slot, new[] { new UnitTriangleIndices((uint)(index * 3), (uint)(index * 3 + 1), (uint)(index * 3 + 2)) })).ToArray();
		var rawMesh = new UnitRawMeshData(0, 1, 0, 0, sections, sections.SelectMany(section => section.Triangles).ToArray(), vertices);
		var bindings = materialSlots.Select((slot, index) => new UnitMaterialBinding(slot, materialIds[index])).ToArray();
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), new[] { stream }, new[] { meshInfo }, bindings, Array.Empty<UnitRawMeshSummary>(), new[] { rawMesh });
	}

}