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
	public void Write_RebuildsSharedStreamVertexAndIndexOffsetsIndependently()
	{
		var toc = new byte[1024];
		var stream = new UnitStreamInfo(0, 128, 0, 1, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0,
			[new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12)]);
		var first = CreateRawMesh(0, 10, 100, 3, 1);
		var second = CreateRawMesh(1, 0, 0, 6, 2);
		var model = new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), [stream], [CreateMesh(0, 256, 10, 100), CreateMesh(1, 512, 0, 0)], Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), [first, second]);

		var result = new UnitMeshWriter().Write(model, toc);

		Assert.Equal(0u, ReadUInt32(result.TocData, 512 + 4));
		Assert.Equal(6u, ReadUInt32(result.TocData, 256 + 4));
		Assert.Equal(0u, ReadUInt32(result.TocData, 512 + 12));
		Assert.Equal(6u, ReadUInt32(result.TocData, 256 + 12));
		var indexBufferOffset = ReadUInt32(result.TocData, 128 + 8 + 320 + 96);
		Assert.Equal(6u, BinaryPrimitives.ReadUInt16LittleEndian(result.GpuData.AsSpan((int)indexBufferOffset + 12, 2)));
	}

	[Fact]
	public void Write_PromotesSharedStreamTo32BitIndicesWhenCombinedVerticesExceed16BitRange()
	{
		var toc = new byte[1024];
		var stream = new UnitStreamInfo(0, 128, 0, 1, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0,
			[new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12)]);
		var first = CreateRawMesh(0, 0, 0, 65536, 1);
		var second = CreateRawMesh(1, 65535, 3, 3, 1);
		second = second with
		{
			Sections = [new UnitRawMeshSectionData(0, 0, [new UnitTriangleIndices(0, 1, 2)])],
			Triangles = [new UnitTriangleIndices(0, 1, 2)]
		};
		var model = new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), [stream], [CreateMesh(0, 256, 0, 0), CreateMesh(1, 512, 65535, 3)], Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), [first, second]);

		var result = new UnitMeshWriter().Write(model, toc);

		Assert.True(result.GpuData.Length >= 65539 * 12 + 24);
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

	private static UnitMeshInfo CreateMesh(int index, uint offset, uint vertexOffset, uint indexOffset)
		=> new(index, offset, (uint)(100 + index), 0, 0, 0, 1, offset + 128, 1, offset + 132, UnitMeshSemanticInfo.Empty(0, index), [0],
			[new UnitMeshSectionInfo(offset, 0, 0, vertexOffset, 0, indexOffset, 0, 0)]);

	private static UnitRawMeshData CreateRawMesh(int index, uint vertexOffset, uint indexOffset, int vertexCount, int triangleCount)
	{
		var vertices = Enumerable.Range(0, vertexCount).Select(value => new UnitRawVertexRecord((uint)value, new byte[12], Array.Empty<UnitVertexComponentValue>())).ToArray();
		var triangles = Enumerable.Range(0, triangleCount).Select(_ => new UnitTriangleIndices(0, 1, 2)).ToArray();
		return new UnitRawMeshData(index, (uint)(100 + index), 0, 0, [new UnitRawMeshSectionData(0, 0, triangles)], triangles, vertices);
	}

	private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	}

	private static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}