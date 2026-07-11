using System.Text;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// 浣滅敤锛氳В鏋?HD2 Bones 璧勬簮涓殑楠ㄩ hash 涓庡悕绉拌〃銆?
// Purpose: Parses bone hash and name tables from HD2 Bones resources.
public sealed class UnitBoneNamesReader
{
	public UnitBoneNames Read(ReadOnlySpan<byte> tocData)
	{
		try
		{
			return ReadCore(tocData);
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidDataException or OverflowException or DecoderFallbackException)
		{
			return UnitBoneNames.Empty;
		}
	}

	private static UnitBoneNames ReadCore(ReadOnlySpan<byte> data)
	{
		EnsureRange(data, 0, 8, "bones header");
		var nameCount = checked((int)ReadUInt32(data, 0));
		var lodCount = checked((int)ReadUInt32(data, 4));
		var cursor = 8;
		EnsureRange(data, cursor, checked(lodCount * 4 + nameCount * 4 + lodCount * 4), "bones tables");
		cursor += checked(lodCount * 4);

		var hashes = new uint[nameCount];
		for (var i = 0; i < hashes.Length; i++)
		{
			hashes[i] = ReadUInt32(data, cursor);
			cursor += 4;
		}
		cursor += checked(lodCount * 4);

		var namesBlob = data[cursor..];
		var namesText = Encoding.UTF8.GetString(namesBlob);
		var names = namesText.Split('\0', StringSplitOptions.RemoveEmptyEntries);
		if (names.Length < hashes.Length)
		{
			return UnitBoneNames.Empty;
		}

		return new UnitBoneNames(hashes, names.Take(hashes.Length).ToArray());
	}

	private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string label)
	{
		if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
		{
			throw new InvalidDataException($"Invalid {label} range offset={offset} length={length} dataLength={data.Length}.");
		}
	}

	private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
	{
		return (uint)data[offset]
			| ((uint)data[offset + 1] << 8)
			| ((uint)data[offset + 2] << 16)
			| ((uint)data[offset + 3] << 24);
	}
}