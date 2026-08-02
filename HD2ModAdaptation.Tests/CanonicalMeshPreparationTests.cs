using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies canonical PrepareMesh/GetMeshData normalization and fail-closed ABI encoding.
public sealed class CanonicalMeshPreparationTests
{
	[Fact]
	public void Prepare_NormalizesWeightsAndKeepsFourLargestInfluences()
	{
		var source = Mesh([Component(7, "bone_weight", 31, "vec4_half", [1, 2, 3, 4, 5])]);
		var result = new CanonicalMeshPreparation().TryPrepare(source, Stream(8, [StreamComponent(7, 31, "vec4_half", 0, 8)]));

		Assert.True(result.IsValid);
		var weights = Assert.Single(result.Mesh!.Vertices).Components.Single().FloatValues;
		Assert.Equal(4, weights.Length);
		Assert.Equal(1f, weights.Sum(), 4);
		Assert.Equal(new[] { 5f / 14f, 4f / 14f, 3f / 14f, 2f / 14f }, weights);
	}

	[Fact]
	public void Prepare_ReordersBoneIndicesTogetherWithTheirNormalizedWeights()
	{
		var source = Mesh([
			Component(0, "position", 2, "vec3_float", [0, 0, 0]),
			new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, [], [10, 20, 30, 40, 50], []),
			Component(7, "bone_weight", 31, "vec4_half", [1, 2, 3, 4, 5])
		]);
		var result = new CanonicalMeshPreparation().TryPrepare(source, Stream(24, [
			StreamComponent(0, 2, "vec3_float", 0, 12),
			StreamComponent(6, 28, "vec4_uint8", 0, 4),
			StreamComponent(7, 31, "vec4_half", 0, 8)
		]));

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		var vertex = Assert.Single(result.Mesh!.Vertices);
		Assert.Equal(new uint[] { 50, 40, 30, 20 }, vertex.Components.Single(component => component.Type == 6).UIntValues);
		Assert.Equal(new[] { 5f / 14f, 4f / 14f, 3f / 14f, 2f / 14f }, vertex.Components.Single(component => component.Type == 7).FloatValues);
	}

	[Fact]
	public void Prepare_RejectsOutOfRangeIndices()
	{
		var mesh = Mesh([Component(0, "position", 2, "vec3_float", [0, 0, 0])], new(0, 1, 9));
		var result = new CanonicalMeshPreparation().TryPrepare(mesh, Stream(12, [StreamComponent(0, 2, "vec3_float", 0, 12)]));

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "IndexOutOfRange");
	}

	[Fact]
	public void Prepare_RejectsUnknownTargetComponentFormat()
	{
		var result = new CanonicalMeshPreparation().TryPrepare(
			Mesh([Component(0, "position", 99, "unknown", [0, 0, 0])]),
			Stream(16, [StreamComponent(0, 99, "unknown", 0, 16)]));

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UnsupportedComponentFormat");
	}

	[Fact]
	public void Prepare_RebuildsDataAndComponentsWithMatchingStride()
	{
		var result = new CanonicalMeshPreparation().TryPrepare(
			Mesh([Component(0, "position", 2, "vec3_float", [1, 2, 3])]),
			Stream(12, [StreamComponent(0, 2, "vec3_float", 0, 12)]));

		Assert.True(result.IsValid);
		var vertex = Assert.Single(result.Mesh!.Vertices);
		Assert.Equal(12, vertex.Data.Length);
		Assert.Equal(12, vertex.Components.Sum(component => component.RawData.Length));
		Assert.Equal(vertex.Data, vertex.Components.SelectMany(component => component.RawData).ToArray());
	}

	[Fact]
	public void Prepare_EncodesAdditionalBoneIndexGroupsWhenTheSdkProfileDeclaresThem()
	{
		var mesh = Mesh([
			Component(0, "position", 2, "vec3_float", [1, 2, 3]),
			new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, [], [0, 0, 0, 0], [])
		]);
		var stream = Stream(20, [
			StreamComponent(0, 2, "vec3_float", 0, 12),
			StreamComponent(6, 28, "vec4_uint8", 0, 4),
			StreamComponent(6, 28, "vec4_uint8", 1, 4)
		]);

		var result = new CanonicalMeshPreparation().TryPrepare(mesh, stream);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		var vertex = Assert.Single(result.Mesh!.Vertices);
		Assert.Equal(2, vertex.Components.Count(component => component.Type == 6));
		Assert.Equal(20, vertex.Data.Length);
	}

	private static UnitRawMeshData Mesh(IReadOnlyList<UnitVertexComponentValue> components, UnitTriangleIndices? triangle = null)
	{
		var triangles = triangle is null ? Array.Empty<UnitTriangleIndices>() : new[] { triangle };
		var section = new UnitRawMeshSectionData(0, 0, triangles);
		return new(0, 1, 0, 0, [section], triangles, [new UnitRawVertexRecord(0, [], components)]);
	}

	private static UnitVertexComponentValue Component(uint type, string typeName, uint format, string formatName, float[] values)
		=> new(type, typeName, format, formatName, 0, values, [], []);

	private static UnitStreamInfo Stream(uint stride, IReadOnlyList<UnitStreamComponentInfo> components)
		=> new(0, 0, 0, (ulong)components.Count, 0, 1, stride, 0, 3, 0, 0, 0, 0, 0, components);

	private static UnitStreamComponentInfo StreamComponent(uint type, uint format, string formatName, uint index, uint size)
		=> new(type, type.ToString(), format, formatName, index, 0, size);
}
