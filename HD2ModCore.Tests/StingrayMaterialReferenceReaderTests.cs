using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 Stingray material texture id 表解析逻辑。
// Purpose: Verifies parsing of Stingray material texture id tables.
public sealed class StingrayMaterialReferenceReaderTests
{
	[Fact]
	public void ReadTextureIds_ReturnsTextureReferencesAfterVariableData()
	{
		var data = new byte[136 + 8 + 16 + 40 + 12];
		WriteUInt32(data, 64, 2);
		WriteUInt32(data, 104, 1);
		WriteUInt64(data, 144, 0x1111111111111111ul);
		WriteUInt64(data, 152, 0x2222222222222222ul);
		var reader = new StingrayMaterialReferenceReader();

		var textureIds = reader.ReadTextureIds(data);

		Assert.Equal([0x1111111111111111ul, 0x2222222222222222ul], textureIds);
	}

	private static void WriteUInt32(byte[] buffer, int offset, uint value)
	{
		buffer[offset + 0] = (byte)value;
		buffer[offset + 1] = (byte)(value >> 8);
		buffer[offset + 2] = (byte)(value >> 16);
		buffer[offset + 3] = (byte)(value >> 24);
	}

	private static void WriteUInt64(byte[] buffer, int offset, ulong value)
	{
		WriteUInt32(buffer, offset, (uint)value);
		WriteUInt32(buffer, offset + 4, (uint)(value >> 32));
	}
}