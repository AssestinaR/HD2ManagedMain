using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.Processing;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class BoneRemapperTests
{
	[Fact]
	public void TryMap_WithValidMapping_ReturnsTrue()
	{
		// Arrange
		var sourceBoneInfo = new UnitBoneInfo(
			Index: 0,
			Offset: 0,
			NumBones: 3,
			MatrixOffset: 0,
			RealIndicesOffset: 0,
			RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20, 30 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1, 2 }) });

		var targetBoneInfo = new UnitBoneInfo(
			Index: 0,
			Offset: 0,
			NumBones: 3,
			MatrixOffset: 0,
			RealIndicesOffset: 0,
			RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20, 30 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1, 2 }) });

		var remapPairs = new[]
		{
			new BoneRemapPair(0, sourceBoneInfo.Remaps[0], targetBoneInfo.Remaps[0])
		};

		var remapper = new BoneRemapper(sourceBoneInfo, targetBoneInfo, remapPairs);

		// Act
		var result = remapper.TryMap(0, 0, out var targetIndex);

		// Assert
		Assert.True(result);
		Assert.Equal(0u, targetIndex);
	}

	[Fact]
	public void TryMap_WithInvalidMapping_ReturnsFalse()
	{
		// Arrange
		var sourceBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 3, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20, 30 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1, 2 }) });

		var targetBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 3, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 40, 50, 60 }, // Different real indices
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1, 2 }) });

		var remapPairs = new[]
		{
			new BoneRemapPair(0, sourceBoneInfo.Remaps[0], targetBoneInfo.Remaps[0])
		};

		var remapper = new BoneRemapper(sourceBoneInfo, targetBoneInfo, remapPairs);

		// Act
		var result = remapper.TryMap(0, 0, out var targetIndex);

		// Assert
		Assert.False(result);
		Assert.Equal(0u, targetIndex);
	}

	[Fact]
	public void TryMap_WithMatchingRealIndices_MapsCorrectly()
	{
		// Arrange
		var sourceBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 3, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 100, 200, 300 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1, 2 }) });

		var targetBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 3, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 100, 200, 300 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 2, 0, 1 }) }); // Different order

		var remapPairs = new[]
		{
			new BoneRemapPair(0, sourceBoneInfo.Remaps[0], targetBoneInfo.Remaps[0])
		};

		var remapper = new BoneRemapper(sourceBoneInfo, targetBoneInfo, remapPairs);

		// Act & Assert
		Assert.True(remapper.TryMap(0, 0, out var target0));
		Assert.Equal(1u, target0); // realIndex 100 -> target fake index 1

		Assert.True(remapper.TryMap(1, 0, out var target1));
		Assert.Equal(2u, target1); // realIndex 200 -> target fake index 2

		Assert.True(remapper.TryMap(2, 0, out var target2));
		Assert.Equal(0u, target2); // realIndex 300 -> target fake index 0
	}

	[Fact]
	public void TryMap_WithMultipleMaterials_UsesMaterialIndex()
	{
		// Arrange
		var sourceBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 3, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20, 30 },
			Remaps: new[]
			{
				new UnitBoneRemap(0, 0, new uint[] { 0, 1 }),  // Material 0
				new UnitBoneRemap(1, 0, new uint[] { 1, 2 })   // Material 1
			});

		var targetBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 3, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20, 30 },
			Remaps: new[]
			{
				new UnitBoneRemap(0, 0, new uint[] { 0, 1 }),
				new UnitBoneRemap(1, 0, new uint[] { 1, 2 })
			});

		var remapPairs = new[]
		{
			new BoneRemapPair(0, sourceBoneInfo.Remaps[0], targetBoneInfo.Remaps[0]),
			new BoneRemapPair(1, sourceBoneInfo.Remaps[1], targetBoneInfo.Remaps[1])
		};

		var remapper = new BoneRemapper(sourceBoneInfo, targetBoneInfo, remapPairs);

		// Act & Assert - Material 0
		Assert.True(remapper.TryMap(0, 0, out var mat0_idx0));
		Assert.Equal(0u, mat0_idx0);

		// Act & Assert - Material 1
		Assert.True(remapper.TryMap(0, 1, out var mat1_idx0));
		Assert.Equal(0u, mat1_idx0);
	}

	[Fact]
	public void TryMap_WithFallback_UsesMaterialZeroMapping()
	{
		// Arrange
		var sourceBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 2, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1 }) });

		var targetBoneInfo = new UnitBoneInfo(
			Index: 0, Offset: 0, NumBones: 2, MatrixOffset: 0, RealIndicesOffset: 0, RemapDataOffset: 0,
			RealIndices: new uint[] { 10, 20 },
			Remaps: new[] { new UnitBoneRemap(0, 0, new uint[] { 0, 1 }) });

		var remapPairs = new[]
		{
			new BoneRemapPair(0, sourceBoneInfo.Remaps[0], targetBoneInfo.Remaps[0])
		};

		var remapper = new BoneRemapper(sourceBoneInfo, targetBoneInfo, remapPairs);

		// Act - Try with non-existent material index
		var result = remapper.TryMap(0, 999, out var targetIndex);

		// Assert - Should fall back to material 0
		Assert.True(result);
		Assert.Equal(0u, targetIndex);
	}
}
