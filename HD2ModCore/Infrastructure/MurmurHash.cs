using System.Text;

namespace HD2ModCore.Infrastructure;

// 作用：实现 HD2SDK 使用的 Murmur64/Murmur32 资源名哈希算法。
// Purpose: Implements the Murmur64/Murmur32 resource-name hash algorithm used by HD2SDK.
internal static class MurmurHash
{
	private const ulong Multiplier = 0xc6a4a7935bd1e995;
	private const int Rotate = 47;

	public static uint Murmur32(string value)
		=> (uint)(Murmur64(Encoding.UTF8.GetBytes(value)) >> 32);

	public static ulong Murmur64(ReadOnlySpan<byte> data, ulong seed = 0)
	{
		var hash = seed ^ unchecked(Multiplier * (ulong)data.Length);
		var alignedLength = data.Length / 8 * 8;
		for (var offset = 0; offset < alignedLength; offset += 8)
		{
			var key = ReadUInt64(data, offset);
			key = unchecked(key * Multiplier);
			key ^= key >> Rotate;
			key = unchecked(key * Multiplier);
			hash ^= key;
			hash = unchecked(hash * Multiplier);
		}

		var tailLength = data.Length & 7;
		if (tailLength >= 7) hash ^= (ulong)data[alignedLength + 6] << 48;
		if (tailLength >= 6) hash ^= (ulong)data[alignedLength + 5] << 40;
		if (tailLength >= 5) hash ^= (ulong)data[alignedLength + 4] << 32;
		if (tailLength >= 4) hash ^= (ulong)data[alignedLength + 3] << 24;
		if (tailLength >= 3) hash ^= (ulong)data[alignedLength + 2] << 16;
		if (tailLength >= 2) hash ^= (ulong)data[alignedLength + 1] << 8;
		if (tailLength >= 1)
		{
			hash ^= data[alignedLength];
			hash = unchecked(hash * Multiplier);
		}

		hash ^= hash >> Rotate;
		hash = unchecked(hash * Multiplier);
		hash ^= hash >> Rotate;
		return hash;
	}

	private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
	{
		return (ulong)data[offset]
			| ((ulong)data[offset + 1] << 8)
			| ((ulong)data[offset + 2] << 16)
			| ((ulong)data[offset + 3] << 24)
			| ((ulong)data[offset + 4] << 32)
			| ((ulong)data[offset + 5] << 40)
			| ((ulong)data[offset + 6] << 48)
			| ((ulong)data[offset + 7] << 56);
	}
}