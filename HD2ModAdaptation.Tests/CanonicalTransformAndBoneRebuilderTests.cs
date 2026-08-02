using System.Numerics;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies canonical mesh-local transform resolution and fail-closed bone rebuilding.
public sealed class CanonicalTransformAndBoneRebuilderTests
{
	[Fact]
	public void TransformResolver_UsesTransformInfoIndicesForIdentityAndNonIdentity()
	{
		var source = Model(Matrix4x4.CreateTranslation(2, 0, 0), [10]);
		var target = Model(Matrix4x4.CreateTranslation(5, 0, 0), [10]);
		var result = new CanonicalTransformResolver().TryResolve(source, 0, target, 0);

		Assert.True(result.IsValid);
		Assert.Equal(new Vector3(-3, 0, 0), Vector3.Transform(Vector3.Zero, result.SourceToTargetLocal!.Value));
	}

	[Fact]
	public void Merger_AppliesResolvedNonIdentityTransform()
	{
		var source = Model(Matrix4x4.CreateTranslation(2, 0, 0), [10]);
		var target = Model(Matrix4x4.CreateTranslation(5, 0, 0), [10]);
		var resolved = new CanonicalTransformResolver().TryResolve(source, 0, target, 0);
		var raw = new UnitRawMeshData(0, 1, 0, 0, [new(0, 10, [new(0, 1, 2)])], [new(0, 1, 2)],
		[
			new(0, [], [new(0, "position", 0, "vec3_float", 0, [0, 0, 0], [], [])]),
			new(1, [], [new(0, "position", 0, "vec3_float", 0, [1, 0, 0], [], [])]),
			new(2, [], [new(0, "position", 0, "vec3_float", 0, [0, 1, 0], [], [])])
		]);

		var merged = new CanonicalMeshSemanticMerger().TryMerge(
			new CanonicalMeshSemanticMergeRequest(
				new(new HD2ModAdaptation.PatchReconstruction.AssetKey(1, 1), 0),
				new(new HD2ModAdaptation.PatchReconstruction.AssetKey(1, 2), 0),
				resolved.SourceToTargetLocal!.Value), raw, raw);

		Assert.True(merged.IsValid, string.Join("; ", merged.Diagnostics.Select(item => item.Message)));
		Assert.Equal(-3f, merged.Mesh!.Vertices[0].Components[0].FloatValues[0]);
	}

	[Fact]
	public void TransformResolver_RejectsMissingOrNonInvertibleTransform()
	{
		var source = Model(Matrix4x4.Identity, [10]);
		var missing = Model(Matrix4x4.Identity, Array.Empty<uint>()) with
		{
			Meshes = [source.Meshes[0] with { TransformIndex = 1 }]
		};
		var missingResult = new CanonicalTransformResolver().TryResolve(source, 0, missing, 0);
		Assert.False(missingResult.IsValid);
		Assert.Contains(missingResult.Diagnostics, item => item.Code == "MissingTransformInfoMatrix");

		var singular = Model(Matrix4x4.CreateScale(0, 1, 1), [10]);
		var singularResult = new CanonicalTransformResolver().TryResolve(source, 0, singular, 0);
		Assert.False(singularResult.IsValid);
		Assert.Contains(singularResult.Diagnostics, item => item.Code == "NonInvertibleTargetTransform");
	}

	[Fact]
	public void BoneRebuilder_MapsHashAndRemapAndRejectsMissingTargetBone()
	{
		var source = Model(Matrix4x4.Identity, [100, 200], boneInfo: new UnitBoneInfo(0, 0, 1, 0, 0, 0, [0], [new UnitBoneRemap(0, 0, [0])]));
		var target = Model(Matrix4x4.Identity, [200, 100], boneInfo: new UnitBoneInfo(0, 0, 0, 0, 0, 0, [], []));
		var sourceRaw = SkinnedRaw(0, 0);
		var targetRaw = SkinnedRaw(0, 0);
		var result = new CanonicalBoneRebuilder().TryRebuild(source, sourceRaw, target, targetRaw);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(new uint[] { 1 }, result.BoneInfo!.RealIndices);
		Assert.Equal(0u, result.Mesh!.Vertices[0].Components.Single(item => item.Type == 6).UIntValues[0]);

		var missing = Model(Matrix4x4.Identity, [200], boneInfo: new UnitBoneInfo(0, 0, 0, 0, 0, 0, [], []));
		var rejected = new CanonicalBoneRebuilder().TryRebuild(source, sourceRaw, missing, targetRaw);
		Assert.False(rejected.IsValid);
		Assert.Contains(rejected.Diagnostics, item => item.Code == "MissingTargetBone");
	}

	[Fact]
	public void BoneRebuilder_RebuildsEmptyTargetPaletteAfterTransformExpansion()
	{
		var source = Model(Matrix4x4.Identity, [100], boneInfo: new UnitBoneInfo(0, 0, 1, 0, 0, 0, [0], [new UnitBoneRemap(0, 0, [0])]));
		var target = Model(Matrix4x4.Identity, [100], boneInfo: new UnitBoneInfo(0, 0, 0, 0, 0, 0, [], []));

		var result = new CanonicalBoneRebuilder().TryRebuild(source, SkinnedRaw(0, 0), target, SkinnedRaw(0, 0));

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(new uint[] { 0 }, result.BoneInfo!.RealIndices);
	}

	private static UnitRawMeshData SkinnedRaw(int meshIndex, uint fakeIndex)
	{
		var vertex = new UnitRawVertexRecord(0, [], [new UnitVertexComponentValue(6, "bone_index", 0, "vec4_uint8", 0, [], [fakeIndex, 0, 0, 0], [])]);
		var triangle = new UnitTriangleIndices(0, 0, 0);
		return new(meshIndex, 1, 0, 0, [new(0, 10, [triangle])], [triangle], [vertex]);
	}

	private static UnitMeshModel Model(Matrix4x4 matrix, IReadOnlyList<uint> hashes, UnitBoneInfo? boneInfo = null)
	{
		var mesh = new UnitMeshInfo(0, 64, 1, 0, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(0, 0), [10], [new(128, 0, 10, 0, 3, 0, 0, 0)]);
		var transform = new UnitTransformMatrix([matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44]);
		var matrices = Enumerable.Repeat(transform, hashes.Count).ToArray();
		return new(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, boneInfo is null ? [] : [boneInfo], [], [mesh], [new(10, 100)], [], [])
		{
			TransformInfo = new UnitTransformInfo(0, 0, 0, [], matrices, [], hashes),
			TransformInfoOffset = 1,
			TransformNameHashes = hashes
		};
	}
}
