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
}