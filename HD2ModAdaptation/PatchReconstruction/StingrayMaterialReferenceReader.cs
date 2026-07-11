using System.Buffers.Binary;

namespace HD2ModAdaptation.PatchReconstruction;

// Purpose: Reads texture asset ids from the Stingray material payload texture table.
public sealed class StingrayMaterialReferenceReader
{
	public IReadOnlyList<ulong> ReadTextureIds(ReadOnlySpan<byte> tocData)
	{
		const int textureCountOffset = 64;
		const int variableCountOffset = 104;
		const int textureRecordsOffset = 136;
		const int variableRecordSize = 20;
		if (tocData.Length < textureRecordsOffset) throw new InvalidDataException("Material payload is too small.");
		var textureCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tocData.Slice(textureCountOffset, 4)));
		var variableCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tocData.Slice(variableCountOffset, 4)));
		var textureIdsOffset = checked(textureRecordsOffset + textureCount * 4);
		var textureIdsEnd = checked(textureIdsOffset + textureCount * 8);
		_ = checked(textureIdsEnd + variableCount * variableRecordSize);
		if (tocData.Length < textureIdsEnd) throw new EndOfStreamException("Material texture reference table is truncated.");
		var textureIds = new ulong[textureCount];
		for (var index = 0; index < textureIds.Length; index++)
		{
			textureIds[index] = BinaryPrimitives.ReadUInt64LittleEndian(tocData.Slice(textureIdsOffset + index * 8, 8));
		}

		return textureIds;
	}
}