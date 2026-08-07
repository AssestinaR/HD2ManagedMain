using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies shared Canonical mesh routing keeps LodIndex=-1 proxy meshes out of BoneInfo and palette workflows.
public sealed class CanonicalMeshSkinningRouterTests
{
	[Fact]
	public void TryPrepare_StaticProxyTarget_StripsSourceSkinningAndExcludesPalette()
	{
		var triangle = new UnitTriangleIndices(0, 0, 0);
		var sourceMesh = new UnitRawMeshData(0, 1, 0, 0, [new(0, 7, [triangle])], [triangle],
		[
			new(0, [1],
			[
				new UnitVertexComponentValue(0, "position", 1, "vec3_float", 0, [0, 0, 0], [], []),
				new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, [], [0, 0, 0, 0], []),
				new UnitVertexComponentValue(7, "bone_weight", 35, "vec4_half", 0, [1, 0, 0, 0], [], [])
			])
		]);
		var targetMesh = sourceMesh with { MeshInfoIndex = 1, LodIndex = -1 };
		var stream = new UnitStreamInfo(0, 0, 0, 0, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0, [new(0, "position", 1, "vec3_float", 0, 0, 12)]);
		var source = Model(sourceMesh, 0);
		var target = Model(targetMesh, -1);

		var result = new CanonicalMeshSkinningRouter().TryPrepare(source, sourceMesh, target, targetMesh, stream);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.True(result.IsProxy);
		Assert.False(result.ParticipatesInLodPalette);
		Assert.Null(result.ProvisionalBoneInfo);
		Assert.DoesNotContain(result.Mesh!.Vertices[0].Components, component => component.Type is 6 or 7);
		Assert.Empty(result.Mesh.Vertices[0].Data);
	}

	private static UnitMeshModel Model(UnitRawMeshData raw, int lodIndex)
	{
		var mesh = new UnitMeshInfo(raw.MeshInfoIndex, 0, raw.MeshId, lodIndex, 0, raw.StreamIndex, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(lodIndex, raw.MeshInfoIndex), [7], [new(0, 0, 7, 0, 0, 0, 0, 0)]);
		return new UnitMeshModel(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [], [mesh], [], [], [raw]);
	}
}