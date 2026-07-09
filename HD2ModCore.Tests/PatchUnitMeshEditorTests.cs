using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchUnitMeshEditor 只生成 dry-run 写回 payload，不直接修改 patch 文件。
// Purpose: Verifies PatchUnitMeshEditor produces dry-run rewritten payloads without directly modifying patch files.
public sealed class PatchUnitMeshEditorTests
{
	[Fact]
	public async Task MinifyRawMeshAsync_UnitEntry_ReturnsWritablePayloadsWithoutMutatingFiles()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var tocData = BuildMinimalUnitTocData();
			var gpuData = BuildMinimalGpuData();
			await File.WriteAllBytesAsync(patchPath, tocData);
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", gpuData);
			var originalPatchBytes = await File.ReadAllBytesAsync(patchPath);
			var originalGpuBytes = await File.ReadAllBytesAsync(patchPath + ".gpu_resources");
			var entry = BuildUnitEntry(patchPath, tocData.Length, gpuData.Length);

			var editor = CreateEditor();
			var result = await editor.MinifyRawMeshAsync(entry, 0);
			var reparsed = new UnitMeshReader().Read(result.TocData, result.GpuResourceData);

			Assert.Equal(entry, result.Entry);
			Assert.Equal(0, result.TocDataSizeDelta);
			Assert.Equal(-22, result.GpuResourceSizeDelta);
			Assert.Equal(originalPatchBytes, await File.ReadAllBytesAsync(patchPath));
			Assert.Equal(originalGpuBytes, await File.ReadAllBytesAsync(patchPath + ".gpu_resources"));
			var rawMesh = Assert.Single(reparsed.RawMeshData);
			Assert.Equal([0f, 0f, 0f], rawMesh.Vertices[0].Components[0].FloatValues);
			Assert.Equal([0.001f, 0f, 0f], rawMesh.Vertices[1].Components[0].FloatValues);
			Assert.Equal([0f, 0.001f, 0f], rawMesh.Vertices[2].Components[0].FloatValues);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ReplaceRawMeshAsync_CompatibleSource_ReturnsTargetPayloadWithSourceVertices()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var targetPath = Path.Combine(tmp, "target.patch_0");
		var sourcePath = Path.Combine(tmp, "source.patch_0");

		try
		{
			var tocData = BuildMinimalUnitTocData();
			var targetGpuData = BuildMinimalGpuData();
			var sourceGpuData = BuildReplacementGpuData();
			await File.WriteAllBytesAsync(targetPath, tocData);
			await File.WriteAllBytesAsync(targetPath + ".gpu_resources", targetGpuData);
			await File.WriteAllBytesAsync(sourcePath, tocData);
			await File.WriteAllBytesAsync(sourcePath + ".gpu_resources", sourceGpuData);

			var editor = CreateEditor();
			var result = await editor.ReplaceRawMeshAsync(
				BuildUnitEntry(targetPath, tocData.Length, targetGpuData.Length),
				0,
				BuildUnitEntry(sourcePath, tocData.Length, sourceGpuData.Length),
				0);
			var reparsed = new UnitMeshReader().Read(result.TocData, result.GpuResourceData);

			var rawMesh = Assert.Single(reparsed.RawMeshData);
			Assert.Equal(123u, rawMesh.Sections[0].MaterialSlotId);
			Assert.Equal([10f, 20f, 30f], rawMesh.Vertices[0].Components[0].FloatValues);
			Assert.Equal([40f, 50f, 60f], rawMesh.Vertices[1].Components[0].FloatValues);
			Assert.Equal([70f, 80f, 90f], rawMesh.Vertices[2].Components[0].FloatValues);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	private static PatchUnitMeshEditor CreateEditor()
		=> new(
			new PatchUnitMeshReader(new PatchEntryPayloadReader(), new UnitMeshReader()),
			new UnitMeshMinifier(),
			new UnitMeshRetargeter(),
			new UnitMeshWriter());

	private static PatchTocEntry BuildUnitEntry(string patchPath, int tocSize, int gpuSize)
		=> new(
			new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x7336c6b662522f0e),
			patchPath,
			Path.GetFileName(patchPath),
			TocDataOffset: 0,
			GpuResourceOffset: 0,
			TocDataSize: (uint)tocSize,
			GpuResourceSize: (uint)gpuSize);

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
		WriteUInt32(data, 0x2c, 0x00A4CD36u);
		WriteUInt32(data, 0x5c, streamInfoOffset);
		WriteUInt32(data, 0x60, 0x3f0);
		WriteUInt32(data, 0x64, meshInfoOffset);
		WriteUInt32(data, 0x70, materialsOffset);

		WriteUInt32(data, streamInfoOffset, 1);
		WriteUInt32(data, streamInfoOffset + 4, streamRecordOffset);
		WriteUInt32(data, streamInfoOffset + 8, 0x12345678u);
		var stream = streamInfoOffset + streamRecordOffset;
		WriteUInt64(data, stream, 0xabcdeful);
		WriteUInt32(data, stream + 8, 0);
		WriteUInt32(data, stream + 12, 2);
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
		var meshCursor = meshInfoOffset + meshRecordOffset + 40;
		WriteUInt32(data, meshCursor, 0x12345678u); meshCursor += 4;
		meshCursor += 16;
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

	private static byte[] BuildReplacementGpuData()
	{
		var data = new byte[64];
		WriteSingle(data, 0, 10f);
		WriteSingle(data, 4, 20f);
		WriteSingle(data, 8, 30f);
		WriteSingle(data, 12, 40f);
		WriteSingle(data, 16, 50f);
		WriteSingle(data, 20, 60f);
		WriteSingle(data, 24, 70f);
		WriteSingle(data, 28, 80f);
		WriteSingle(data, 32, 90f);
		WriteUInt16(data, 36, 0);
		WriteUInt16(data, 38, 2);
		WriteUInt16(data, 40, 1);
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
