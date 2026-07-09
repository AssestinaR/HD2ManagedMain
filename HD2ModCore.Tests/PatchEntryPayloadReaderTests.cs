using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchEntryPayloadReader 会按 patch entry 的 offset/size 读取 TOC 与 sidecar payload，并拒绝越界范围。
// Purpose: Verifies PatchEntryPayloadReader reads TOC and sidecar payloads by entry offset/size and rejects invalid ranges.
public sealed class PatchEntryPayloadReaderTests
{
	[Fact]
	public async Task ReadPayloadAsync_ValidRanges_ReturnsExpectedSlices()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			await File.WriteAllBytesAsync(patchPath, Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
			await File.WriteAllBytesAsync(patchPath + ".stream", Enumerable.Range(100, 40).Select(i => (byte)i).ToArray());
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", Enumerable.Range(200, 48).Select(i => (byte)i).ToArray());

			var entry = new PatchTocEntry(
				new AssetKey(1, 2),
				patchPath,
				Path.GetFileName(patchPath),
				TocDataOffset: 10,
				StreamOffset: 8,
				GpuResourceOffset: 12,
				TocDataSize: 5,
				StreamSize: 6,
				GpuResourceSize: 7,
				EntryIndex: 3);

			var reader = new PatchEntryPayloadReader();
			var payload = await reader.ReadPayloadAsync(entry);

			Assert.Equal(entry, payload.Entry);
			Assert.Equal(new byte[] { 10, 11, 12, 13, 14 }, payload.TocData);
			Assert.Equal(new byte[] { 108, 109, 110, 111, 112, 113 }, payload.StreamData);
			Assert.Equal(new byte[] { 212, 213, 214, 215, 216, 217, 218 }, payload.GpuResourceData);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ReadPayloadAsync_ZeroSidecarSizes_DoesNotRequireSidecarFiles()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			await File.WriteAllBytesAsync(patchPath, new byte[] { 1, 2, 3, 4 });
			var entry = new PatchTocEntry(new AssetKey(1, 2), patchPath, Path.GetFileName(patchPath), TocDataOffset: 1, TocDataSize: 2);

			var reader = new PatchEntryPayloadReader();
			var payload = await reader.ReadPayloadAsync(entry);

			Assert.Equal(new byte[] { 2, 3 }, payload.TocData);
			Assert.Empty(payload.StreamData);
			Assert.Empty(payload.GpuResourceData);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ReadPayloadAsync_TocRangeOutsidePatch_Throws()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			await File.WriteAllBytesAsync(patchPath, new byte[] { 1, 2, 3, 4 });
			var entry = new PatchTocEntry(new AssetKey(1, 2), patchPath, Path.GetFileName(patchPath), TocDataOffset: 3, TocDataSize: 2);

			var reader = new PatchEntryPayloadReader();
			await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadPayloadAsync(entry).AsTask());
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ReadPayloadAsync_SidecarRangeInsideAlignedPadding_ReturnsZeroPaddedTail()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			await File.WriteAllBytesAsync(patchPath, new byte[] { 1, 2, 3, 4 });
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", new byte[] { 9, 8, 7 });
			var entry = new PatchTocEntry(
				new AssetKey(1, 2),
				patchPath,
				Path.GetFileName(patchPath),
				TocDataOffset: 0,
				GpuResourceOffset: 2,
				TocDataSize: 1,
				GpuResourceSize: 4);

			var reader = new PatchEntryPayloadReader();
			var payload = await reader.ReadPayloadAsync(entry);

			Assert.Equal(new byte[] { 7, 0, 0, 0 }, payload.GpuResourceData);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}
}
