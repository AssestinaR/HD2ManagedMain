using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies SDK-compatible canonical tiny placeholders preserve target section/material ABI.
public sealed class CanonicalPlaceholderMinifierTests
{
	[Fact]
	public void Tiny_PreservesSectionsAndUsesOnlyFirstSectionTriangle()
	{
		var target = Mesh();
		var stream = Stream(12, [Component(0, 2, "vec3_float", 0, 12)]);

		var result = new CanonicalPlaceholderMinifier().TryMinify(target, stream);

		Assert.True(result.IsValid);
		Assert.Equal(3, result.Mesh!.Vertices.Count);
		Assert.Equal(3, result.Mesh.Sections.Count);
		Assert.Equal([3, 0, 0], result.Mesh.Sections.Select(section => section.Triangles.Count * 3));
		Assert.Equal(target.Sections.Select(section => section.MaterialSlotId), result.Mesh.Sections.Select(section => section.MaterialSlotId));
	}

	[Fact]
	public void Tiny_ContainsEveryTargetStreamComponentAndCanBePrepared()
	{
		var target = Mesh();
		var stream = Stream(32, [
			Component(0, 2, "vec3_float", 0, 12),
			Component(1, 26, "unk_normal", 0, 4),
			Component(4, 29, "vec2_half", 0, 4),
			Component(5, 4, "rgba_r8g8b8a8", 0, 4),
			Component(7, 31, "vec4_half", 0, 8)]);

		var tiny = new CanonicalPlaceholderMinifier().TryMinify(target, stream);
		var prepared = new CanonicalMeshPreparation().TryPrepare(tiny.Mesh!, stream);

		Assert.True(tiny.IsValid);
		Assert.True(prepared.IsValid);
		Assert.All(prepared.Mesh!.Vertices, vertex =>
		{
			Assert.Equal(stream.Components.Select(component => (component.Type, component.Index)), vertex.Components.Select(component => (component.Type, component.Index)));
			Assert.Equal(stream.VertexStride, (uint)vertex.Data.Length);
		});
	}

	private static UnitRawMeshData Mesh()
		=> new(7, 70, 2, 0, [
			new UnitRawMeshSectionData(3, 30, []),
			new UnitRawMeshSectionData(4, 40, []),
			new UnitRawMeshSectionData(5, 50, [])], [], []);

	private static UnitStreamInfo Stream(uint stride, IReadOnlyList<UnitStreamComponentInfo> components)
		=> new(0, 0, 0, (ulong)components.Count, 0, 3, stride, 0, 0, 0, 0, 0, 0, 0, components);

	private static UnitStreamComponentInfo Component(uint type, uint format, string formatName, uint index, uint size)
		=> new(type, type.ToString(), format, formatName, index, 0, size);
}