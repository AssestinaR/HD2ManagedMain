using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchArchiveDryWriter 能 dry-run 重建 patch TOC 与 GPU sidecar，而不写入磁盘。
// Purpose: Verifies PatchArchiveDryWriter can dry-run rebuild patch TOC and GPU sidecar without writing to disk.
public sealed class PatchArchiveDryWriterTests
{
	[Fact]
	public async Task BuildWritePlanAsync_OneEditedEntry_RebuildsTocAndGpuSidecarWithUpdatedMetadata()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var firstKey = new AssetKey(0xe0a48d0be9a7453f, 0x1111111111111111);
			var secondKey = new AssetKey(0xe0a48d0be9a7453f, 0x2222222222222222);
			var firstTocPayload = new byte[] { 1, 2, 3, 4 };
			var secondTocPayload = new byte[] { 5, 6, 7 };
			var firstGpuPayload = new byte[] { 10, 11, 12 };
			var secondGpuPayload = new byte[] { 20, 21, 22, 23 };
			var patchBytes = BuildStandardHeaderPatch(
				patchPath,
				new[] { firstKey, secondKey },
				new[] { firstTocPayload, secondTocPayload },
				new[] { firstGpuPayload, secondGpuPayload },
				out var entries,
				out var gpuBytes);
			await File.WriteAllBytesAsync(patchPath, patchBytes);
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", gpuBytes);
			var originalPatchBytes = await File.ReadAllBytesAsync(patchPath);
			var originalGpuBytes = await File.ReadAllBytesAsync(patchPath + ".gpu_resources");
			var editedTocPayload = new byte[] { 9, 8, 7, 6, 5 };
			var editedGpuPayload = new byte[] { 90, 91 };
			var edit = new PatchUnitMeshEditResult(
				entries[0],
				new PatchEntryPayload(entries[0], firstTocPayload, Array.Empty<byte>(), firstGpuPayload),
				CreateEmptyModel(),
				CreateEmptyModel(),
				editedTocPayload,
				editedGpuPayload);

			var writer = new PatchArchiveDryWriter(new PatchTocScanner(), new PatchEntryPayloadReader());
			var plan = await writer.BuildWritePlanAsync(patchPath, new[] { edit });

			Assert.Equal(originalPatchBytes, await File.ReadAllBytesAsync(patchPath));
			Assert.Equal(originalGpuBytes, await File.ReadAllBytesAsync(patchPath + ".gpu_resources"));
			Assert.Equal(2, plan.Entries.Count);
			Assert.Single(plan.EditedPlacements);
			Assert.Equal(editedTocPayload, ReadSlice(plan.TocFileData, plan.Entries[0].TocDataOffset, plan.Entries[0].TocDataSize));
			Assert.Equal(secondTocPayload, ReadSlice(plan.TocFileData, plan.Entries[1].TocDataOffset, plan.Entries[1].TocDataSize));
			Assert.Equal(editedGpuPayload, ReadSlice(plan.GpuResourceFileData, plan.Entries[0].GpuResourceOffset, plan.Entries[0].GpuResourceSize));
			Assert.Equal(secondGpuPayload, ReadSlice(plan.GpuResourceFileData, plan.Entries[1].GpuResourceOffset, plan.Entries[1].GpuResourceSize));
			Assert.Equal(0ul, plan.Entries[0].GpuResourceOffset);
			Assert.Equal(64ul, plan.Entries[1].GpuResourceOffset);
			Assert.Equal(plan.Entries[0].TocDataOffset, ReadUInt64(plan.TocFileData, 120));
			Assert.Equal(plan.Entries[0].GpuResourceOffset, ReadUInt64(plan.TocFileData, 136));
			Assert.Equal((uint)editedTocPayload.Length, ReadUInt32(plan.TocFileData, 160));
			Assert.Equal((uint)editedGpuPayload.Length, ReadUInt32(plan.TocFileData, 168));
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task BuildWritePlanAsync_ForeignEdit_Throws()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var key = new AssetKey(0xe0a48d0be9a7453f, 0x1111111111111111);
			var patchBytes = BuildStandardHeaderPatch(
				patchPath,
				new[] { key },
				new[] { new byte[] { 1, 2, 3 } },
				new[] { new byte[] { 4, 5, 6 } },
				out var entries,
				out var gpuBytes);
			await File.WriteAllBytesAsync(patchPath, patchBytes);
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", gpuBytes);
			var foreignEntry = entries[0] with { SourceFilePath = Path.Combine(tmp, "other.patch_0") };
			var edit = new PatchUnitMeshEditResult(
				foreignEntry,
				new PatchEntryPayload(foreignEntry, new byte[] { 1, 2, 3 }, Array.Empty<byte>(), new byte[] { 4, 5, 6 }),
				CreateEmptyModel(),
				CreateEmptyModel(),
				new byte[] { 7 },
				new byte[] { 8 });

			var writer = new PatchArchiveDryWriter(new PatchTocScanner(), new PatchEntryPayloadReader());
			await Assert.ThrowsAsync<InvalidDataException>(() => writer.BuildWritePlanAsync(patchPath, new[] { edit }).AsTask());
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task BuildWritePlanAsync_RemovedEntry_RewritesHeaderTypeCountsAndEntryIndices()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var patchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");

		try
		{
			var firstKey = new AssetKey(0xe0a48d0be9a7453f, 0x1111111111111111);
			var secondKey = new AssetKey(0xe0a48d0be9a7453f, 0x2222222222222222);
			var patchBytes = BuildStandardHeaderPatch(
				patchPath,
				new[] { firstKey, secondKey },
				new[] { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6, 7 } },
				new[] { new byte[] { 8, 9 }, new byte[] { 10, 11, 12 } },
				out var entries,
				out var gpuBytes);
			await File.WriteAllBytesAsync(patchPath, patchBytes);
			await File.WriteAllBytesAsync(patchPath + ".gpu_resources", gpuBytes);

			var writer = new PatchArchiveDryWriter(new PatchTocScanner(), new PatchEntryPayloadReader());
			var plan = await writer.BuildWritePlanAsync(patchPath, Array.Empty<PatchUnitMeshEditResult>(), new[] { entries[0] });

			Assert.Single(plan.Entries);
			Assert.Equal(secondKey, plan.Entries[0].AssetKey);
			Assert.Equal(0u, plan.Entries[0].EntryIndex);
			Assert.Equal(1u, ReadUInt32(plan.TocFileData, 8));
			Assert.Equal(1ul, ReadUInt64(plan.TocFileData, 88));
			Assert.Equal(secondKey.FileId, ReadUInt64(plan.TocFileData, 104));
			Assert.Equal(secondKey.TypeId, ReadUInt64(plan.TocFileData, 112));
			Assert.Equal(0u, ReadUInt32(plan.TocFileData, 180));
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	private static byte[] BuildStandardHeaderPatch(
		string patchPath,
		AssetKey[] keys,
		byte[][] tocPayloads,
		byte[][] gpuPayloads,
		out PatchTocEntry[] entries,
		out byte[] gpuBytes)
	{
		const uint magic = 4026531857;
		var groups = keys.GroupBy(e => e.TypeId).ToList();
		var entriesOffset = 72 + groups.Count * 32;
		var entryTableEnd = entriesOffset + keys.Length * 80;
		var tocLength = entryTableEnd + tocPayloads.Sum(p => p.Length);
		var tocBytes = new byte[tocLength];
		WriteUInt32(tocBytes, 0, magic);
		WriteUInt32(tocBytes, 4, (uint)groups.Count);
		WriteUInt32(tocBytes, 8, (uint)keys.Length);

		var typeOffset = 72;
		foreach (var group in groups)
		{
			WriteUInt64(tocBytes, typeOffset + 8, group.Key);
			WriteUInt64(tocBytes, typeOffset + 16, (ulong)group.Count());
			WriteUInt32(tocBytes, typeOffset + 24, 16);
			WriteUInt32(tocBytes, typeOffset + 28, 64);
			typeOffset += 32;
		}

		entries = new PatchTocEntry[keys.Length];
		using var gpuStream = new MemoryStream();
		var tocCursor = entryTableEnd;
		for (var i = 0; i < keys.Length; i++)
		{
			var entryOffset = entriesOffset + i * 80;
			WriteUInt64(tocBytes, entryOffset, keys[i].FileId);
			WriteUInt64(tocBytes, entryOffset + 8, keys[i].TypeId);
			WriteUInt64(tocBytes, entryOffset + 16, (ulong)tocCursor);
			WriteUInt32(tocBytes, entryOffset + 56, (uint)tocPayloads[i].Length);
			WriteUInt32(tocBytes, entryOffset + 76, (uint)i);
			tocPayloads[i].CopyTo(tocBytes, tocCursor);

			PadToAlignment(gpuStream, 64);
			var gpuOffset = (ulong)gpuStream.Position;
			gpuStream.Write(gpuPayloads[i]);
			WriteUInt64(tocBytes, entryOffset + 32, gpuOffset);
			WriteUInt32(tocBytes, entryOffset + 64, (uint)gpuPayloads[i].Length);
			entries[i] = new PatchTocEntry(
				keys[i],
				patchPath,
				Path.GetFileName(patchPath),
				(ulong)tocCursor,
				GpuResourceOffset: gpuOffset,
				TocDataSize: (uint)tocPayloads[i].Length,
				GpuResourceSize: (uint)gpuPayloads[i].Length,
				EntryIndex: (uint)i);
			tocCursor += tocPayloads[i].Length;
		}

		gpuBytes = gpuStream.ToArray();
		return tocBytes;
	}

	private static UnitMeshModel CreateEmptyModel()
		=> new(
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			Array.Empty<UnitBoneInfo>(),
			Array.Empty<UnitStreamInfo>(),
			Array.Empty<UnitMeshInfo>(),
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			Array.Empty<UnitRawMeshData>());

	private static byte[] ReadSlice(byte[] data, ulong offset, uint size)
	{
		var result = new byte[size];
		Array.Copy(data, checked((long)offset), result, 0, size);
		return result;
	}

	private static void PadToAlignment(Stream stream, int alignment)
	{
		var padding = (alignment - (int)(stream.Position % alignment)) % alignment;
		for (var i = 0; i < padding; i++)
		{
			stream.WriteByte(0);
		}
	}

	private static uint ReadUInt32(byte[] buffer, int offset)
		=> (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);

	private static ulong ReadUInt64(byte[] buffer, int offset)
		=> ReadUInt32(buffer, offset) | ((ulong)ReadUInt32(buffer, offset + 4) << 32);

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
