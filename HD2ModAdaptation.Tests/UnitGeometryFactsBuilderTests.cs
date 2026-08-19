using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class UnitGeometryFactsBuilderTests
{
	[Fact]
	public void Analyze_PrefersRenderableLod0OverTinyPlaceholder()
	{
		var placeholder = Raw(0, 0, vertexCount: 3, triangleCount: 1, sectionsOnly: false);
		var renderable = Raw(1, 0, vertexCount: 4, triangleCount: 2, sectionsOnly: false);
		var model = Model(placeholder, renderable);

		var facts = UnitGeometryFactsBuilder.Analyze(model);

		Assert.Equal(UnitMeshGeometryQuality.Placeholder, facts.FindMesh(0)!.Quality);
		Assert.Equal(UnitMeshGeometryQuality.RenderableLod0, facts.FindMesh(1)!.Quality);
		Assert.Equal(1, UnitGeometryFactsBuilder.SelectBestRenderableLod0(model)!.MeshInfoIndex);
	}

	[Fact]
	public void HasRenderableGeometry_CountsSectionTrianglesWhenFlatListIsEmpty()
	{
		var raw = Raw(0, 0, vertexCount: 4, triangleCount: 2, sectionsOnly: true);

		Assert.True(UnitGeometryFactsBuilder.HasRenderableGeometry(raw));
		Assert.Equal(2, UnitGeometryFactsBuilder.CountTriangles(raw));
	}

	private static UnitMeshModel Model(params UnitRawMeshData[] rawMeshes)
	{
		var meshes = rawMeshes.Select(raw => new UnitMeshInfo(
			raw.MeshInfoIndex, 0, raw.MeshId, raw.LodIndex, 0, raw.StreamIndex, 1, 0, 1, 0,
			new UnitMeshSemanticInfo($"mesh_{raw.MeshInfoIndex}", "Torso", "Armor", "Any", string.Empty, raw.LodIndex, raw.MeshInfoIndex, false, false, false),
			[], [])).ToArray();
		return new UnitMeshModel(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [], meshes, [], [], rawMeshes);
	}

	private static UnitRawMeshData Raw(int meshInfoIndex, int lodIndex, int vertexCount, int triangleCount, bool sectionsOnly)
	{
		var triangles = Enumerable.Range(0, triangleCount).Select(index => new UnitTriangleIndices(0, 1, 2)).ToArray();
		var vertices = Enumerable.Range(0, vertexCount).Select(index => new UnitRawVertexRecord((uint)index, [1], [])).ToArray();
		return new UnitRawMeshData(meshInfoIndex, (uint)(meshInfoIndex + 1), lodIndex, 0,
			sectionsOnly ? [new UnitRawMeshSectionData(0, 0, triangles)] : [],
			sectionsOnly ? [] : triangles,
			vertices);
	}
}
