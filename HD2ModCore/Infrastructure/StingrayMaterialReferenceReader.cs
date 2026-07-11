using HD2ModCore.Infrastructure.Binary;

namespace HD2ModCore.Infrastructure;

// 作用：读取 Stingray material payload 中引用的 texture ids，用于 SDK-style 依赖闭合检查。
// Purpose: Reads texture ids referenced by a Stingray material payload for SDK-style dependency closure checks.
public sealed class StingrayMaterialReferenceReader
{
	public IReadOnlyList<ulong> ReadTextureIds(ReadOnlySpan<byte> tocData)
	{
		const int numTexturesOffset = 64;
		const int numVariablesOffset = 104;
		const int textureRecordsOffset = 136;
		const int variableRecordSize = 20;

		if (tocData.Length < textureRecordsOffset)
		{
			throw new InvalidDataException("Material payload is too small.");
		}

		var numTextures = checked((int)BinaryPrimitivesLE.ReadUInt32(tocData.Slice(numTexturesOffset, 4)));
		var numVariables = checked((int)BinaryPrimitivesLE.ReadUInt32(tocData.Slice(numVariablesOffset, 4)));
		var textureIdsOffset = checked(textureRecordsOffset + numTextures * 4);
		var variableRecordsOffset = checked(textureIdsOffset + numTextures * 8);
		_ = checked(variableRecordsOffset + numVariables * variableRecordSize);
		var textureIdsEnd = checked(textureIdsOffset + numTextures * 8);
		if (tocData.Length < textureIdsEnd)
		{
			throw new EndOfStreamException("Material texture reference table is truncated.");
		}

		var textureIds = new ulong[numTextures];
		for (var i = 0; i < textureIds.Length; i++)
		{
			textureIds[i] = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(textureIdsOffset + i * 8, 8));
		}

		return textureIds;
	}
}