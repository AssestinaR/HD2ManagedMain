using System.Buffers.Binary;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Reads only the root Unit material binding table for low-cost dependency analysis.
public sealed class UnitMaterialReferenceReader : IUnitMaterialReferenceReader
{
	private const int MaterialsOffsetFieldOffset = 0x70;
	private const int MaterialsOffsetFieldLength = sizeof(uint);

	public IReadOnlyList<UnitMaterialBinding> ReadBindings(ReadOnlySpan<byte> tocData)
		=> ReadReferenceBindings(tocData)
			.Select(reference => new UnitMaterialBinding(reference.SectionId, reference.MaterialId))
			.ToArray();

	public IReadOnlyList<UnitMaterialReferenceBinding> ReadReferenceBindings(ReadOnlySpan<byte> tocData)
	{
		if (tocData.Length < MaterialsOffsetFieldOffset + MaterialsOffsetFieldLength)
		{
			throw new InvalidDataException("Unit TocData is too small to contain the MaterialsOffset field.");
		}

		var materialsOffset = BinaryPrimitives.ReadUInt32LittleEndian(tocData.Slice(MaterialsOffsetFieldOffset, MaterialsOffsetFieldLength));
		if (materialsOffset == 0)
		{
			return Array.Empty<UnitMaterialReferenceBinding>();
		}

		if (materialsOffset > tocData.Length - sizeof(uint))
		{
			throw new InvalidDataException("Unit MaterialsOffset is outside TocData.");
		}

		var offset = checked((int)materialsOffset);
		var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tocData.Slice(offset, sizeof(uint))));
		var tableOffset = checked(offset + sizeof(uint));
		var tableLength = checked(count * (sizeof(uint) + sizeof(ulong)));
		if (tableOffset > tocData.Length || tableLength > tocData.Length - tableOffset)
		{
			throw new InvalidDataException("Unit material binding table is truncated.");
		}

		var sectionIds = new uint[count];
		for (var index = 0; index < count; index++)
		{
			sectionIds[index] = BinaryPrimitives.ReadUInt32LittleEndian(tocData.Slice(tableOffset + index * sizeof(uint), sizeof(uint)));
		}

		var materialIdsOffset = checked(tableOffset + count * sizeof(uint));
		var result = new UnitMaterialReferenceBinding[count];
		for (var index = 0; index < count; index++)
		{
			var materialIdOffset = checked(materialIdsOffset + index * sizeof(ulong));
			var materialId = BinaryPrimitives.ReadUInt64LittleEndian(tocData.Slice(materialIdOffset, sizeof(ulong)));
			result[index] = new UnitMaterialReferenceBinding(sectionIds[index], materialId, checked((uint)materialIdOffset));
		}

		return result;
	}
}

public interface IUnitMaterialReferenceReader
{
	IReadOnlyList<UnitMaterialBinding> ReadBindings(ReadOnlySpan<byte> tocData);
	IReadOnlyList<UnitMaterialReferenceBinding> ReadReferenceBindings(ReadOnlySpan<byte> tocData);
}

public sealed record UnitMaterialReferenceBinding(
	uint SectionId,
	ulong MaterialId,
	uint MaterialIdPayloadRelativeOffset);