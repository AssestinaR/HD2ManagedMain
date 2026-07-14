using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies compact whole-entry subset output preserves all three payload sections.
public sealed class PatchSubsetWriterTests : IDisposable
{
	private const ulong UnitType = 0xe0a48d0be9a7453f;
	private const ulong MaterialType = 0xeac0b497876adedf;
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task WriteAsync_WritesOnlySelectedEntriesAndPreservesPayloads()
	{
		var sourceDirectory = Path.Combine(root, "source");
		var outputDirectory = Path.Combine(root, "output");
		Directory.CreateDirectory(sourceDirectory);
		var source = Path.Combine(sourceDirectory, "subset.patch_0");
		var entries = new[]
		{
			new SourceEntry(new AssetKey(UnitType, 1), [1, 2], [3, 4], [5, 6, 7]),
			new SourceEntry(new AssetKey(MaterialType, 2), [8, 9], [10], [11, 12]),
		};
		WritePatch(source, entries);

		var result = await new PatchSubsetWriter().WriteAsync(source, outputDirectory, [entries[1].Key]);

		var entry = Assert.Single(await new PatchTocScanner().ScanEntriesAsync(result.TocFilePath));
		Assert.Equal(entries[1].Key, entry.AssetKey);
		Assert.Equal((ulong)0, entry.StreamOffset);
		Assert.Equal((ulong)0, entry.GpuResourceOffset);
		var payload = await new PatchEntryPayloadReader().ReadPayloadAsync(entry);
		Assert.Equal(entries[1].TocData, payload.TocData);
		Assert.Equal(entries[1].StreamData, payload.StreamData);
		Assert.Equal(entries[1].GpuData, payload.GpuResourceData);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
	}

	private static void WritePatch(string path, IReadOnlyList<SourceEntry> entries)
	{
		const int typeOffset = 60;
		var types = entries.Select(entry => entry.Key.TypeId).Distinct().ToArray();
		var entryOffset = typeOffset + types.Length * 32;
		var payloadOffset = entryOffset + entries.Count * 80;
		var toc = new byte[payloadOffset + entries.Sum(entry => entry.TocData.Length)];
		BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(0, 4), 0xf0000011);
		BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(4, 4), (uint)types.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(8, 4), (uint)entries.Count);
		for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(typeOffset + typeIndex * 32 + 8, 8), types[typeIndex]);
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(typeOffset + typeIndex * 32 + 16, 8), (ulong)entries.Count(entry => entry.Key.TypeId == types[typeIndex]));
		}

		using var stream = new MemoryStream();
		using var gpu = new MemoryStream();
		var tocOffset = payloadOffset;
		for (var index = 0; index < entries.Count; index++)
		{
			var entry = entries[index];
			var offset = entryOffset + index * 80;
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(offset, 8), entry.Key.FileId);
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(offset + 8, 8), entry.Key.TypeId);
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(offset + 16, 8), (ulong)tocOffset);
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(offset + 24, 8), (ulong)stream.Position);
			BinaryPrimitives.WriteUInt64LittleEndian(toc.AsSpan(offset + 32, 8), (ulong)gpu.Position);
			BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(offset + 56, 4), (uint)entry.TocData.Length);
			BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(offset + 60, 4), (uint)entry.StreamData.Length);
			BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(offset + 64, 4), (uint)entry.GpuData.Length);
			BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(offset + 76, 4), (uint)(index + 1));
			entry.TocData.CopyTo(toc, tocOffset);
			tocOffset += entry.TocData.Length;
			stream.Write(entry.StreamData);
			gpu.Write(entry.GpuData);
		}

		File.WriteAllBytes(path, toc);
		File.WriteAllBytes(path + ".stream", stream.ToArray());
		File.WriteAllBytes(path + ".gpu_resources", gpu.ToArray());
	}

	private sealed record SourceEntry(AssetKey Key, byte[] TocData, byte[] StreamData, byte[] GpuData);
}