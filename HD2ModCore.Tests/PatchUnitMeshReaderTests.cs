using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchUnitMeshReader 能从单个 Unit patch entry 读取 payload 并解析 Unit mesh。
// Purpose: Verifies PatchUnitMeshReader reads payload from one Unit patch entry and parses its Unit mesh.
public sealed class PatchUnitMeshReaderTests
{
	[Fact]
	public async Task ReadUnitMeshAsync_UnitEntry_ReturnsParsedMeshAndPayload()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var tocPrefix = Enumerable.Repeat((byte)0xCC, 16).ToArray();
			var tocData = BuildMinimalUnitTocData();
			var gpuPrefix = Enumerable.Repeat((byte)0xDD, 8).ToArray();
			var gpuData = BuildMinimalGpuData();
			await File.WriteAllBytesAsync(patchPath, tocPrefix.Concat(tocData).ToArray());
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", gpuPrefix.Concat(gpuData).ToArray());

			var entry = new PatchTocEntry(
				new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x7336c6b662522f0e),
				patchPath,
				Path.GetFileName(patchPath),
				TocDataOffset: (ulong)tocPrefix.Length,
				GpuResourceOffset: (ulong)gpuPrefix.Length,
				TocDataSize: (uint)tocData.Length,
				GpuResourceSize: (uint)gpuData.Length,
				EntryIndex: 9);

			var reader = new PatchUnitMeshReader(new PatchEntryPayloadReader(), new UnitMeshReader());
			var patchUnit = await reader.ReadUnitMeshAsync(entry);

			Assert.Equal(entry, patchUnit.Entry);
			Assert.Equal(tocData, patchUnit.Payload.TocData);
			Assert.Equal(gpuData, patchUnit.Payload.GpuResourceData);
			Assert.Equal(0x00A4CD36u, patchUnit.Model.Version);
			Assert.Single(patchUnit.Model.Meshes);
			Assert.Single(patchUnit.Model.RawMeshData);
			Assert.Equal(3, patchUnit.Model.RawMeshData[0].Vertices.Count);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ReadUnitMeshAsync_NonUnitEntry_Throws()
	{
		var entry = new PatchTocEntry(new AssetKey(0x1111, 0x2222), "unused.patch_0", "unused.patch_0");
		var reader = new PatchUnitMeshReader(new PatchEntryPayloadReader(), new UnitMeshReader());

		var ex = await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadUnitMeshAsync(entry).AsTask());
		Assert.Contains("not a Unit resource", ex.Message);
	}

	private static byte[] BuildMinimalUnitTocData()
	{
		var data = new byte[0x400];

		const int streamInfoOffset = 0x80;
		const int streamRecordOffset = 0x20;
		const int meshInfoOffset = 0x260;
		const int meshRecordOffset = 0x20;
		const int materialsOffset = 0x340;

		WriteUInt64(data, 0x00, 0x1122334455667788ul);
		WriteUInt64(data, 0x08, 0x0102030405060708ul);
		WriteUInt64(data, 0x10, 0);
		WriteUInt32(data, 0x2c, 0x00A4CD36u);
		WriteUInt32(data, 0x58, 0);
		WriteUInt32(data, 0x5c, streamInfoOffset);
		WriteUInt32(data, 0x60, 0x3f0);
		WriteUInt32(data, 0x64, meshInfoOffset);
		WriteUInt32(data, 0x70, materialsOffset);

		WriteUInt32(data, streamInfoOffset, 1);
		WriteUInt32(data, streamInfoOffset + 4, streamRecordOffset);
		WriteUInt32(data, streamInfoOffset + 8, 0x12345678u);
		WriteUInt32(data, streamInfoOffset + 12, 0);

		var stream = streamInfoOffset + streamRecordOffset;
		WriteUInt64(data, stream, 0xabcdeful);
		WriteUInt32(data, stream + 8, 0);
		WriteUInt32(data, stream + 12, 2);
		WriteUInt32(data, stream + 16, 0);
		WriteUInt64(data, stream + 20, 0);
		var streamFields = stream + 8 + 320;
		WriteUInt64(data, streamFields, 1); streamFields += 8;
		WriteUInt64(data, streamFields, 0x1000); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt32(data, streamFields, 3); streamFields += 4;
		WriteUInt32(data, streamFields, 12); streamFields += 4;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt64(data, streamFields, 0x2000); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt32(data, streamFields, 3); streamFields += 4;
		WriteUInt32(data, streamFields, 0); streamFields += 4;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt32(data, streamFields, 0); streamFields += 4;
		WriteUInt32(data, streamFields, 36); streamFields += 4;
		WriteUInt32(data, streamFields, 36); streamFields += 4;
		WriteUInt32(data, streamFields, 6);

		WriteUInt32(data, meshInfoOffset, 1);
		WriteUInt32(data, meshInfoOffset + 4, meshRecordOffset);
		WriteUInt32(data, meshInfoOffset + 8, 0x12345678u);

		var mesh = meshInfoOffset + meshRecordOffset;
		var meshCursor = mesh + 40;
		WriteUInt32(data, meshCursor, 0x12345678u); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		meshCursor += 40;
		WriteUInt32(data, meshCursor, 1); meshCursor += 4;
		WriteUInt32(data, meshCursor, 112); meshCursor += 4;
		WriteUInt64(data, meshCursor, 0); meshCursor += 8;
		WriteUInt32(data, meshCursor, 1); meshCursor += 4;
		WriteUInt32(data, meshCursor, 116); meshCursor += 4;
		WriteUInt32(data, meshCursor, 123); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0);

		WriteUInt32(data, materialsOffset, 1);
		WriteUInt32(data, materialsOffset + 4, 123);
		WriteUInt64(data, materialsOffset + 8, 0x8877665544332211ul);

		return data;
	}

	private static byte[] BuildMinimalGpuData()
	{
		var data = new byte[64];
		WriteSingle(data, 0, 1f);
		WriteSingle(data, 4, 2f);
		WriteSingle(data, 8, 3f);
		WriteSingle(data, 12, 4f);
		WriteSingle(data, 16, 5f);
		WriteSingle(data, 20, 6f);
		WriteSingle(data, 24, 7f);
		WriteSingle(data, 28, 8f);
		WriteSingle(data, 32, 9f);
		WriteUInt16(data, 36, 0);
		WriteUInt16(data, 38, 1);
		WriteUInt16(data, 40, 2);
		return data;
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}

	private static void WriteUInt16(byte[] data, int offset, ushort value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
	}

	private static void WriteSingle(byte[] data, int offset, float value) => WriteInt32(data, offset, BitConverter.SingleToInt32Bits(value));

	private static void WriteInt32(byte[] data, int offset, int value) => WriteUInt32(data, offset, unchecked((uint)value));

	private static void WriteUInt64(byte[] data, int offset, ulong value)
	{
		WriteUInt32(data, offset, (uint)value);
		WriteUInt32(data, offset + 4, (uint)(value >> 32));
	}
}
