using System.Numerics;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies SDK-style shared LOD palette compilation before final stream encoding.
public sealed class CanonicalLodBonePaletteCompilerTests
{
	[Fact]
	public void Compile_AggregatesMultipleMeshesWithOneLodTransform()
	{
		var target = Target([10, 20], 0, 0);
		var first = Mesh(0, 0, 0);
		var second = Mesh(1, 0, 0);
		var firstInfo = BoneInfo(0);
		var secondInfo = BoneInfo(1);

		var result = new CanonicalLodBonePaletteCompiler().TryCompile(target, 0, [
			new CanonicalLodBoneInput(first, firstInfo),
			new CanonicalLodBoneInput(second, secondInfo)
		]);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(new uint[] { 0, 1 }, result.BoneInfo!.RealIndices);
		Assert.Single(result.BoneInfo.Remaps);
		Assert.Equal(new uint[] { 0, 1 }, result.BoneInfo.Remaps[0].FakeIndices);
		Assert.Equal(2, result.Meshes.Count);
	}

	[Fact]
	public void Compile_RejectsSharedLodWithDifferentMeshTransforms()
	{
		var target = Target([10], 0, 1);
		var result = new CanonicalLodBonePaletteCompiler().TryCompile(target, 0, [
			new CanonicalLodBoneInput(Mesh(0, 0, 0), BoneInfo(0)),
			new CanonicalLodBoneInput(Mesh(1, 0, 0), BoneInfo(0))
		]);

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, item => item.Code == "SharedLodTransformMismatch");
	}

	[Fact]
	public void Compile_UsesContinuousFinalMaterialOrdinalsForNonContinuousTargetSectionIndices()
	{
		var target = Target([10], 0, 0);
		var mesh = Mesh(0, 0, 0) with
		{
			// The target's serialized section index may be non-contiguous, but this is the
			// post-lowering final mesh where SDK remap indexing is the material ordinal 0.
			Sections = [new UnitRawMeshSectionData(0, 7, [new(0, 0, 0)])],
			Triangles = [new(0, 0, 0)]
		};

		var result = new CanonicalLodBonePaletteCompiler().TryCompile(target, 0, [new(mesh, BoneInfo(0))]);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Single(result.BoneInfo!.Remaps);
		Assert.Equal(0, result.BoneInfo.Remaps[0].MaterialIndex);
	}

	[Fact]
	public void Compile_RewritesPerMeshMaterialOrdinalsToOneSharedLodLayout()
	{
		var target = Target([10], 0, 0);
		var first = Mesh(0, 0, 0) with { Sections = [new UnitRawMeshSectionData(0, 7, [new(0, 0, 0)])] };
		var second = Mesh(1, 0, 0) with { Sections = [new UnitRawMeshSectionData(0, 9, [new(0, 0, 0)])] };

		var result = new CanonicalLodBonePaletteCompiler().TryCompile(target, 0, [
			new CanonicalLodBoneInput(first, BoneInfo(0)),
			new CanonicalLodBoneInput(second, BoneInfo(0))
		]);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal([0, 1], result.BoneInfo!.Remaps.Select(remap => remap.MaterialIndex));
		Assert.Equal(0u, result.Meshes.Single(mesh => mesh.MeshInfoIndex == 0).Sections[0].MaterialIndex);
		Assert.Equal(1u, result.Meshes.Single(mesh => mesh.MeshInfoIndex == 1).Sections[0].MaterialIndex);
	}

	[Fact]
	public void Compile_RewritesEveryBoneIndexGroupToTheUnifiedPalette()
	{
		var target = Target([10, 20], 0, 0);
		var mesh = Mesh(0, 0, 0) with
		{
			Vertices = [new UnitRawVertexRecord(0, [], [
				new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, [], [0, 0, 0, 0], []),
				new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 1, [], [1, 0, 0, 0], []),
				new UnitVertexComponentValue(7, "bone_weight", 35, "vec4_half", 0, [1, 0, 0, 0], [], [])])]
		};
		var provisional = new UnitBoneInfo(0, 0, 2, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1])]);

		var result = new CanonicalLodBonePaletteCompiler().TryCompile(target, 0, [new CanonicalLodBoneInput(mesh, provisional)]);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal([0u, 1u], result.BoneInfo!.RealIndices);
		var groups = result.Meshes[0].Vertices[0].Components.Where(component => component.Type == 6).OrderBy(component => component.Index).ToArray();
		Assert.Equal([0u, 0u, 0u, 0u], groups[0].UIntValues);
		Assert.Equal([1u, 0u, 0u, 0u], groups[1].UIntValues);
	}

	[Fact]
	public void Compile_ReportsMeshSectionAndVertexWhenFinalSkinningComponentsAreMissing()
	{
		var target = Target([10], 0, 0);
		var mesh = Mesh(0, 0, 0) with
		{
			Sections = [new UnitRawMeshSectionData(0, 42, [new(0, 0, 0)])],
			Triangles = [new(0, 0, 0)],
			Vertices = [new UnitRawVertexRecord(0, [], [])]
		};

		var result = new CanonicalLodBonePaletteCompiler().TryCompile(target, 0, [new(mesh, BoneInfo(0))]);

		var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "FinalSkinningLayoutMissing");
		Assert.Contains("MeshInfo=0", diagnostic.Message);
		Assert.Contains("Lod=0", diagnostic.Message);
		Assert.Contains("SectionMaterial=0", diagnostic.Message);
		Assert.Contains("Vertex=0", diagnostic.Message);
	}

	private static UnitMeshModel Target(IReadOnlyList<uint> hashes, uint firstTransform, uint secondTransform)
	{
		var matrix = Matrix4x4.Identity;
		var encoded = new UnitTransformMatrix([matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44]);
		var mesh = new UnitMeshInfo(0, 1, 1, 0, firstTransform, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(0, 0), [7], [new(1, 0, 7, 0, 3, 0, 0, 0)]);
		var second = mesh with { Index = 1, TransformIndex = secondTransform };
		return new(1, 1, 0, 0, 0, 1, 1, 1, 0, 0, UnitCustomizationInfo.Empty, [BoneInfo(0)], [], [mesh, second], [], [], [])
		{
			TransformInfo = new UnitTransformInfo(0, 0, 0, [], Enumerable.Repeat(encoded, hashes.Count).ToArray(), [], hashes),
			TransformNameHashes = hashes
		};
	}

	private static UnitRawMeshData Mesh(int meshIndex, int lodIndex, uint fakeIndex)
	{
		var triangle = new UnitTriangleIndices(0, 0, 0);
		var vertex = new UnitRawVertexRecord(0, [], [
			new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, [], [fakeIndex, 0, 0, 0], []),
			new UnitVertexComponentValue(7, "bone_weight", 35, "vec4_half", 0, [1, 0, 0, 0], [], [])
		]);
		return new(meshIndex, 1, lodIndex, 0, [new UnitRawMeshSectionData(0, 7, [triangle])], [triangle], [vertex]);
	}

	private static UnitBoneInfo BoneInfo(uint realIndex)
		=> new(0, 0, 1, 0, 0, 0, [realIndex], [new UnitBoneRemap(0, 0, [0])]);
}
