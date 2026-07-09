using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 TOC 扫描器能从最小构造的 .patch_n 文件中提取 (TypeID, FileID) 资产键。
// Purpose: Verifies TOC scanner extracts (TypeID, FileID) asset keys from a minimal synthetic .patch_n file.
public sealed class PatchTocScannerTests
{
	[Fact]
	public async Task ScanAssetKeysAsync_MinimalToc_ReturnsKeys()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var path = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			await File.WriteAllBytesAsync(path, BuildToc(numTypes: 0, entries: new[]
			{
				new AssetKey(0x1111111111111111, 0x2222222222222222),
				new AssetKey(0xAAAAAAAAAAAAAAAA, 0xBBBBBBBBBBBBBBBB),
			}));

			var scanner = new PatchTocScanner();
			var keys = await scanner.ScanAssetKeysAsync(path);

			Assert.Contains(new AssetKey(0x1111111111111111, 0x2222222222222222), keys);
			Assert.Contains(new AssetKey(0xAAAAAAAAAAAAAAAA, 0xBBBBBBBBBBBBBBBB), keys);
			Assert.Equal(2, keys.Count);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ScanAssetKeysAsync_BadMagic_Throws()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var path = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var bytes = BuildToc(numTypes: 0, entries: new[] { new AssetKey(1, 2) });
			bytes[0] ^= 0xFF;
			await File.WriteAllBytesAsync(path, bytes);

			var scanner = new PatchTocScanner();
			await Assert.ThrowsAsync<InvalidDataException>(() => scanner.ScanAssetKeysAsync(path).AsTask());
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ScanAssetKeysAsync_StandardHeaderToc_ReturnsKeys()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var path = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var unitType = new AssetKey(0xe0a48d0be9a7453f, 0x7336c6b662522f0e);
			var textureType = new AssetKey(0xcd4238c6a0c69e32, 0x1111222233334444);
			await File.WriteAllBytesAsync(path, BuildStandardHeaderToc(new[] { unitType, textureType }));

			var scanner = new PatchTocScanner();
			var keys = await scanner.ScanAssetKeysAsync(path);

			Assert.Contains(unitType, keys);
			Assert.Contains(textureType, keys);
			Assert.Equal(2, keys.Count);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ScanEntriesAsync_StandardHeaderToc_ReturnsEntryOffsetsAndSizes()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var path = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var unit = new AssetKey(0xe0a48d0be9a7453f, 0x7336c6b662522f0e);
			var bytes = BuildStandardHeaderToc(new[] { unit });
			var entryOffset = 72 + 32;
			WriteUInt64(bytes, entryOffset + 16, 0x1000);
			WriteUInt64(bytes, entryOffset + 24, 0x2000);
			WriteUInt64(bytes, entryOffset + 32, 0x3000);
			WriteUInt32(bytes, entryOffset + 56, 0x44);
			WriteUInt32(bytes, entryOffset + 60, 0x55);
			WriteUInt32(bytes, entryOffset + 64, 0x66);
			WriteUInt32(bytes, entryOffset + 76, 7);
			await File.WriteAllBytesAsync(path, bytes);

			var scanner = new PatchTocScanner();
			var entry = Assert.Single(await scanner.ScanEntriesAsync(path));

			Assert.Equal(unit, entry.AssetKey);
			Assert.Equal(0x1000ul, entry.TocDataOffset);
			Assert.Equal(0x2000ul, entry.StreamOffset);
			Assert.Equal(0x3000ul, entry.GpuResourceOffset);
			Assert.Equal(0x44u, entry.TocDataSize);
			Assert.Equal(0x55u, entry.StreamSize);
			Assert.Equal(0x66u, entry.GpuResourceSize);
			Assert.Equal(7u, entry.EntryIndex);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	private static byte[] BuildToc(int numTypes, AssetKey[] entries)
	{
		const uint magic = 4026531857;
		var numFiles = entries.Length;

		// Layout matches PatchTocScanner assumptions:
		// header(12) + padding to 60 + types(numTypes*32) + entries(numFiles*80)
		var entriesOffset = 60 + numTypes * 32;
		var totalSize = entriesOffset + numFiles * 80;
		var buffer = new byte[totalSize];

		WriteUInt32(buffer, 0, magic);
		WriteUInt32(buffer, 4, (uint)numTypes);
		WriteUInt32(buffer, 8, (uint)numFiles);

		var offset = entriesOffset;
		for (var i = 0; i < entries.Length; i++)
		{
			// entry begins with file_id then type_id in the reference implementation
			WriteUInt64(buffer, offset, entries[i].FileId);
			WriteUInt64(buffer, offset + 8, entries[i].TypeId);
			offset += 80;
		}

		return buffer;
	}

	private static byte[] BuildStandardHeaderToc(AssetKey[] entries)
	{
		const uint magic = 4026531857;
		var groups = entries.GroupBy(e => e.TypeId).ToList();
		var entriesOffset = 72 + groups.Count * 32;
		var totalSize = entriesOffset + entries.Length * 80;
		var buffer = new byte[totalSize];

		WriteUInt32(buffer, 0, magic);
		WriteUInt32(buffer, 4, (uint)groups.Count);
		WriteUInt32(buffer, 8, (uint)entries.Length);

		var typeOffset = 72;
		foreach (var group in groups)
		{
			WriteUInt64(buffer, typeOffset + 8, group.Key);
			WriteUInt64(buffer, typeOffset + 16, (ulong)group.Count());
			WriteUInt32(buffer, typeOffset + 24, 16);
			WriteUInt32(buffer, typeOffset + 28, 64);
			typeOffset += 32;
		}

		var entryOffset = entriesOffset;
		foreach (var entry in entries)
		{
			WriteUInt64(buffer, entryOffset, entry.FileId);
			WriteUInt64(buffer, entryOffset + 8, entry.TypeId);
			entryOffset += 80;
		}

		return buffer;
	}

	private static void WriteUInt32(byte[] buffer, int offset, uint value)
	{
		buffer[offset + 0] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
		buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
		buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
	}

	private static void WriteUInt64(byte[] buffer, int offset, ulong value)
	{
		buffer[offset + 0] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
		buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
		buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
		buffer[offset + 4] = (byte)((value >> 32) & 0xFF);
		buffer[offset + 5] = (byte)((value >> 40) & 0xFF);
		buffer[offset + 6] = (byte)((value >> 48) & 0xFF);
		buffer[offset + 7] = (byte)((value >> 56) & 0xFF);
	}
}
