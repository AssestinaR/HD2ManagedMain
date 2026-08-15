using System.Numerics;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class CanonicalAppendMeshAssemblerTests
{
    [Fact]
    public void Append_PreservesTargetTopologyAndOffsetsDecorationIndices()
    {
        var result = new CanonicalAppendMeshAssembler().TryAppend(Mesh(0), Mesh(10), Matrix4x4.Identity);

        Assert.True(result.IsValid);
        Assert.Equal(6, result.Mesh!.Vertices.Count);
        Assert.Equal(2, result.Mesh.Sections.Count);
        Assert.Equal(new UnitTriangleIndices(0, 1, 2), Assert.Single(result.Mesh.Sections[0].Triangles));
        Assert.Equal(new UnitTriangleIndices(3, 4, 5), Assert.Single(result.Mesh.Sections[1].Triangles));
        Assert.True(result.Sections[0].IsTargetSection);
        Assert.False(result.Sections[1].IsTargetSection);
    }

    [Fact]
    public void Append_TransformsDecorationPositionIntoTargetLocalSpace()
    {
        var result = new CanonicalAppendMeshAssembler().TryAppend(Mesh(0), Mesh(0), Matrix4x4.CreateTranslation(5, 0, 0));

        Assert.True(result.IsValid);
        var position = result.Mesh!.Vertices[3].Components.Single(component => component.Type == 0).FloatValues;
        Assert.Equal(new[] { 5f, 0f, 0f }, position);
    }

    private static UnitRawMeshData Mesh(uint material)
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0), Vertex(1, 1, 0), Vertex(2, 0, 1)
        };
        var section = new UnitRawMeshSectionData(0, material, [new UnitTriangleIndices(0, 1, 2)]);
        return new UnitRawMeshData(0, 0, 0, 0, [section], section.Triangles, vertices);
    }

    private static UnitRawVertexRecord Vertex(uint index, float x, float y)
        => new(index, [], [new UnitVertexComponentValue(0, "position", 2, "vec3_float", 0, [x, y, 0], [], [])]);
}
