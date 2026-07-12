using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Locks the SDK 3.8.0 BoneInfo.SetRemap behavior used by the SDK-style reconstruction path.
public sealed class SdkStyleBoneRemapBuilderTests
{
	[Fact]
	public void SetRemap_UsesNumericBoneNamesAsTransformHashes()
	{
		var boneInfo = CreateBoneInfo(new uint[] { 2 });
		var transformHashes = new uint[] { 10, 20, 123456, 30 };

		var result = new SdkStyleBoneRemapBuilder().SetRemap(
			boneInfo,
			new[] { new[] { "123456" } },
			transformHashes);

		var remap = Assert.Single(result.Remaps);
		Assert.Equal(new uint[] { 0 }, remap.FakeIndices);
		Assert.Equal(new uint[] { 2 }, result.RealIndices);
		Assert.Equal(1u, result.NumBones);
	}

	[Fact]
	public void SetRemap_HashesNamedBonesAndAppendsMissingLodRealIndices()
	{
		const string boneName = "spine_01";
		var boneHash = SdkStyleMurmurHash.Murmur32(boneName);
		var boneInfo = CreateBoneInfo(new uint[] { 1 });
		var transformHashes = new uint[] { 10, 20, boneHash };

		var result = new SdkStyleBoneRemapBuilder().SetRemap(
			boneInfo,
			new[] { new[] { boneName } },
			transformHashes);

		var remap = Assert.Single(result.Remaps);
		Assert.Equal(new uint[] { 1 }, remap.FakeIndices);
		Assert.Equal(new uint[] { 1, 2 }, result.RealIndices);
		Assert.Equal(2u, result.NumBones);
	}

	[Fact]
	public void SetRemap_SkipsBonesMissingFromTransformInfo()
	{
		var boneInfo = CreateBoneInfo(new uint[] { 0 });

		var result = new SdkStyleBoneRemapBuilder().SetRemap(
			boneInfo,
			new[] { new[] { "not_in_transform_info" } },
			new uint[] { 1, 2, 3 });

		var remap = Assert.Single(result.Remaps);
		Assert.Empty(remap.FakeIndices);
		Assert.Equal(new uint[] { 0 }, result.RealIndices);
		Assert.Equal(1u, result.NumBones);
	}

	[Fact]
	public void SetRemap_BuildsPerMaterialRemapsAndSdkOffsets()
	{
		var boneInfo = CreateBoneInfo(new uint[] { 0, 2 });
		var transformHashes = new uint[] { 100, 200, 300, 400 };

		var result = new SdkStyleBoneRemapBuilder().SetRemap(
			boneInfo,
			new[]
			{
				new[] { "100", "300" },
				new[] { "400" },
				new[] { "999" }
			},
			transformHashes);

		Assert.Collection(
			result.Remaps,
			remap =>
			{
				Assert.Equal(0, remap.MaterialIndex);
				Assert.Equal(28u, remap.Offset);
				Assert.Equal(new uint[] { 0, 1 }, remap.FakeIndices);
			},
			remap =>
			{
				Assert.Equal(1, remap.MaterialIndex);
				Assert.Equal(36u, remap.Offset);
				Assert.Equal(new uint[] { 2 }, remap.FakeIndices);
			},
			remap =>
			{
				Assert.Equal(2, remap.MaterialIndex);
				Assert.Equal(40u, remap.Offset);
				Assert.Empty(remap.FakeIndices);
			});
		Assert.Equal(new uint[] { 0, 2, 3 }, result.RealIndices);
		Assert.Equal(3u, result.NumBones);
	}

	private static UnitBoneInfo CreateBoneInfo(IReadOnlyList<uint> realIndices)
		=> new(0, 0, (uint)realIndices.Count, 0, 0, 0, realIndices, Array.Empty<UnitBoneRemap>());
}