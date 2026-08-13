using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class CanonicalAutoLodMappingExpanderTests
{
	[Fact]
	public void Expand_CullingSourceWithoutMatchingTargetMeshId_SkipsCullingMapping()
	{
		var sourceKey = new AssetKey(1, 0x10);
		var targetKey = new AssetKey(1, 0x20);
		var source = Model(Raw(0, 0x1111));
		var target = Model(Raw(0, 0x2222));

		var mappings = CanonicalAutoLodMappingExpander.Expand(
			target,
			new Dictionary<AssetKey, UnitMeshModel> { [sourceKey] = source },
			[new CanonicalReplacementMapping(new(sourceKey, 0), new(targetKey, 0))]);

		Assert.Empty(mappings);
	}

	private static UnitMeshModel Model(UnitRawMeshData raw)
	{
		var mesh = new UnitMeshInfo(
			raw.MeshInfoIndex, 0, raw.MeshId, -1, 0, raw.StreamIndex, 1, 0, 1, 0,
			new UnitMeshSemanticInfo("culling", "Torso", "Armor", "Any", string.Empty, -1, raw.MeshInfoIndex, true, false, false),
			[7], [new(0, 0, 7, 0, 3, 0, 3, 0)]);
		return new UnitMeshModel(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [], [mesh], [], [], [raw]);
	}

	private static UnitRawMeshData Raw(int index, uint meshId)
	{
		var triangles = new[] { new UnitTriangleIndices(0, 1, 2), new UnitTriangleIndices(0, 2, 3) };
		var vertices = new[] { Vertex(0), Vertex(1), Vertex(2), Vertex(3) };
		return new UnitRawMeshData(index, meshId, -1, 0, [new(0, 7, triangles)], triangles, vertices);
	}

	private static UnitRawVertexRecord Vertex(uint index) => new(index, [1], []);
}
