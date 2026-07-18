using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies Unit mesh write invariants required by the HD2SDK Stingray unit parser.
public sealed class UnitMeshWriterTests
{
	[Fact]
	public void Write_AppendsSdkEndingBytesWhenEndingOffsetMoves()
	{
		var originalToc = new byte[128];
		var model = CreateModel(meshCount: 2);

		var result = new UnitMeshWriter().Write(model, originalToc);

		Assert.Equal(136, result.TocData.Length);
		Assert.Equal(128u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x60, 4)));
		Assert.Equal(2ul, BinaryPrimitives.ReadUInt64LittleEndian(result.TocData.AsSpan(128, 8)));
	}

	[Fact]
	public void Write_WithBoneInfoRelocation_RebuildsVariableLengthBlockAndShiftsFollowingOffsets()
	{
		var originalToc = new byte[320];
		WriteUInt32(originalToc, 0x58, 128);
		WriteUInt32(originalToc, 0x5c, 164);
		WriteUInt32(originalToc, 0x60, 240);
		WriteUInt32(originalToc, 0x64, 200);
		WriteUInt32(originalToc, 128, 1);
		WriteUInt32(originalToc, 132, 8);
		WriteUInt32(originalToc, 136, 0);
		WriteUInt32(originalToc, 140, 16);
		WriteUInt32(originalToc, 144, 16);
		WriteUInt32(originalToc, 148, 16);
		WriteUInt32(originalToc, 152, 0);
		WriteUInt32(originalToc, 156, 1);
		WriteUInt32(originalToc, 160, 12);
		WriteUInt32(originalToc, 164, 0);
		WriteUInt32(originalToc, 200, 0);

		var boneInfo = new UnitBoneInfo(0, 136, 0, 16, 16, 16, Array.Empty<uint>(), new[] { new UnitBoneRemap(0, 12, new uint[] { 0 }) });
		var model = new UnitMeshModel(0, 0, 0, 0, 0, 128, 164, 200, 0, 240, UnitCustomizationInfo.Empty, new[] { boneInfo }, Array.Empty<UnitStreamInfo>(), Array.Empty<UnitMeshInfo>(), Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), Array.Empty<UnitRawMeshData>());

		var result = new UnitMeshWriter(allowBoneInfoRelocation: true).Write(model, originalToc);

		Assert.Equal(176u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x5c, 4)));
		Assert.Equal(212u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x64, 4)));
		Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(128, 4)));
		Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(132, 4)));
		Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(152, 4)));
		Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(160, 4)));
	}

	[Fact]
	public void Write_WithTransformInfoRelocation_ExpandsBlockAndShiftsFollowingOffsets()
	{
		var originalToc = new byte[512];
		WriteUInt32(originalToc, 0x34, 128);
		WriteUInt32(originalToc, 0x4c, 288);
		WriteUInt32(originalToc, 0x58, 304);
		WriteUInt32(originalToc, 0x5c, 320);
		WriteUInt32(originalToc, 0x60, 400);
		WriteUInt32(originalToc, 0x64, 336);
		WriteUInt32(originalToc, 128, 1);
		var model = new UnitMeshModel(0, 0, 0, 0, 0, 304, 320, 336, 0, 400, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), Array.Empty<UnitStreamInfo>(), Array.Empty<UnitMeshInfo>(), Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), Array.Empty<UnitRawMeshData>())
		{
			TransformInfoOffset = 128,
			TransformInfo = new UnitTransformInfo(1, 2, 3,
				[new UnitLocalTransform(new float[9], new float[3], new float[] { 1, 1, 1 }, 0), new UnitLocalTransform(new float[9], new float[3], new float[] { 1, 1, 1 }, 0)],
				[new UnitTransformMatrix(Identity()), new UnitTransformMatrix(Identity())],
				[new UnitTransformEntry(0, 0), new UnitTransformEntry(1, 0)], [10u, 20u]),
			TransformNameHashes = [10u, 20u]
		};

		var result = new UnitMeshWriter(allowTransformInfoRelocation: true).Write(model, originalToc);

		Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(128, 4)));
		Assert.Equal(416u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x4c, 4)));
		Assert.Equal(432u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x58, 4)));
		Assert.Equal(448u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x5c, 4)));
		Assert.Equal(464u, BinaryPrimitives.ReadUInt32LittleEndian(result.TocData.AsSpan(0x64, 4)));
	}

	private static UnitMeshModel CreateModel(int meshCount)
	{
		var meshes = Enumerable.Range(0, meshCount)
			.Select(index => new UnitMeshInfo(
				index,
				0,
				(uint)(0x1000 + index),
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				UnitMeshSemanticInfo.Empty(0, index),
				Array.Empty<uint>(),
				Array.Empty<UnitMeshSectionInfo>()))
			.ToArray();

		return new UnitMeshModel(
			0,
			0,
			0,
			0,
			0,
			0,
			96,
			64,
			0,
			80,
			UnitCustomizationInfo.Empty,
			Array.Empty<UnitBoneInfo>(),
			Array.Empty<UnitStreamInfo>(),
			meshes,
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			Array.Empty<UnitRawMeshData>());
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	}

	private static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}