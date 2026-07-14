using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the lightweight Unit root material binding parser used by dependency scans.
public sealed class UnitMaterialReferenceReaderTests
{
	[Fact]
	public void ReadBindings_ReadsSectionAndMaterialTables()
	{
		var data = new byte[0xa0];
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x70, 4), 0x74);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x74, 4), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x78, 4), 11);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x7c, 4), 22);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x80, 8), 0x1111111111111111);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x88, 8), 0x2222222222222222);

		var result = new UnitMaterialReferenceReader().ReadBindings(data);

		Assert.Equal([new UnitMaterialBinding(11, 0x1111111111111111), new UnitMaterialBinding(22, 0x2222222222222222)], result);
		Assert.Equal(
			[new UnitMaterialReferenceBinding(11, 0x1111111111111111, 0x80), new UnitMaterialReferenceBinding(22, 0x2222222222222222, 0x88)],
			new UnitMaterialReferenceReader().ReadReferenceBindings(data));
	}

	[Fact]
	public void ReadBindings_RejectsTruncatedTable()
	{
		var data = new byte[0x80];
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x70, 4), 0x74);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x74, 4), 1);

		Assert.Throws<InvalidDataException>(() => new UnitMaterialReferenceReader().ReadBindings(data));
	}
}