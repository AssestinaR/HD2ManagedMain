using System.Buffers.Binary;

namespace HD2ModCore.Infrastructure.Binary;

// 作用：小型工具封装，用于按小端序读取基础数值类型。
// Purpose: Small helper wrapper for little-endian primitive reads.
internal static class BinaryPrimitivesLE
{
	public static uint ReadUInt32(ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt32LittleEndian(span);
	public static ulong ReadUInt64(ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt64LittleEndian(span);
}
