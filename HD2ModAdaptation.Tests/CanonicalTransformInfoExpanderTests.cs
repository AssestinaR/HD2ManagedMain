using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies Canonical Avatar-rig expansion remains independent from the retired SdkStyle route.
public sealed class CanonicalTransformInfoExpanderTests
{
	[Fact]
	public void Expand_AddsMissingPaletteBoneWithAvatarParentChain()
	{
		var target = Model([10]);
		var source = Model([10, 20, 30], new UnitBoneInfo(0, 0, 1, 0, 0, 0, [2], []));
		var sourceRaw = new UnitRawMeshData(0, 1, 0, 0, [], [], []);
		var avatar = TransformInfo([10, 20, 30], [0, 0, 1]);

		var expanded = new CanonicalTransformInfoExpander().Expand(target, [(source, sourceRaw)], avatar);

		Assert.Equal(new uint[] { 10, 20, 30 }, expanded.TransformNameHashes);
		Assert.Equal((ushort)0, expanded.TransformInfo.Entries[1].ParentIndex);
		Assert.Equal((ushort)1, expanded.TransformInfo.Entries[2].ParentIndex);
		Assert.Equal(3, expanded.TransformInfo.Matrices.Count);
	}

	[Fact]
	public void Expand_RejectsPaletteBoneMissingFromAvatar()
	{
		var target = Model([10]);
		var source = Model([10, 99], new UnitBoneInfo(0, 0, 1, 0, 0, 0, [1], []));
		var sourceRaw = new UnitRawMeshData(0, 1, 0, 0, [], [], []);

		var exception = Assert.Throws<InvalidDataException>(() => new CanonicalTransformInfoExpander().Expand(target, [(source, sourceRaw)], TransformInfo([10], [0])));

		Assert.Contains("0x00000063", exception.Message);
	}

	private static UnitMeshModel Model(IReadOnlyList<uint> hashes, UnitBoneInfo? bone = null)
		=> new(1, 1, 0, 0, 0, 0, 1, 1, 0, 0, UnitCustomizationInfo.Empty, bone is null ? [] : [bone], [], [], [], [], [])
		{
			TransformInfoOffset = 1,
			TransformInfo = TransformInfo(hashes, Enumerable.Repeat(0, hashes.Count).ToArray()),
			TransformNameHashes = hashes
		};

	private static UnitTransformInfo TransformInfo(IReadOnlyList<uint> hashes, IReadOnlyList<int> parents)
	{
		var local = new UnitLocalTransform([1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0], [1, 1, 1], 0);
		var matrix = new UnitTransformMatrix([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
		return new UnitTransformInfo(0, 0, 0,
			Enumerable.Repeat(local, hashes.Count).ToArray(),
			Enumerable.Repeat(matrix, hashes.Count).ToArray(),
			parents.Select((parent, index) => new UnitTransformEntry(1, checked((ushort)parent))).ToArray(), hashes);
	}
}