using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies static Canonical placements receive an explicit target-rig anchor and valid skinning components.
public sealed class CanonicalStaticMeshBinderTests
{
	[Fact]
	public void TryBind_TargetMeshTransform_UsesMeshTransformAsExplicitAnchor()
	{
		var stream = new UnitStreamInfo(0, 1, 0, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0,
		[
			new(0, "position", 1, "vec3_float", 0, 0, 12),
			new(6, "bone_index", 28, "vec4_uint8", 0, 0, 4),
			new(7, "bone_weight", 35, "vec4_half", 0, 0, 8)
		]);
		var section = new UnitRawMeshSectionData(0, 10, [new(0, 0, 0)]);
		var targetRaw = new UnitRawMeshData(0, 1, 0, 0, [section], section.Triangles, []);
		var target = new UnitMeshModel(1, 1, 0, 0, 0, 1, 1, 1, 0, 0, UnitCustomizationInfo.Empty,
			[new UnitBoneInfo(0, 0, 0, 0, 0, 0, [], [])], [stream],
			[new UnitMeshInfo(0, 1, 1, 0, 1, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(0, 0), [10], [new(1, 0, 10, 0, 0, 0, 0, 0)])], [new(10, 100)], [], [targetRaw])
		{
			TransformInfo = TransformInfo(), TransformNameHashes = [100, 200]
		};
		var staticSource = new UnitRawMeshData(0, 1, 0, 0, [section], section.Triangles,
			[new UnitRawVertexRecord(0, [], [new UnitVertexComponentValue(0, "position", 1, "vec3_float", 0, [0, 0, 0], [], [])])]);

		var result = new CanonicalStaticMeshBinder().TryBind(target, targetRaw, staticSource, stream);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(new uint[] { 1 }, result.BoneInfo!.RealIndices);
		Assert.Equal(new uint[] { 0, 0, 0, 0 }, result.Mesh!.Vertices[0].Components.Single(component => component.Type == 6).UIntValues);
		Assert.Equal(new float[] { 1, 0, 0, 0 }, result.Mesh.Vertices[0].Components.Single(component => component.Type == 7).FloatValues);
		Assert.Empty(Assert.Single(result.Mesh.Vertices).Data);
	}

	private static UnitTransformInfo TransformInfo()
	{
		var local = new UnitLocalTransform([1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0], [1, 1, 1], 0);
		var matrix = new UnitTransformMatrix([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
		return new UnitTransformInfo(0, 0, 0, [local, local], [matrix, matrix], [new(1, 0), new(1, 0)], [100, 200]);
	}
}