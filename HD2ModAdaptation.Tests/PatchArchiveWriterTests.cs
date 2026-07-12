using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies that direct patch reconstruction preserves archive layout and writes replacement payloads.
public sealed class PatchArchiveWriterTests : IDisposable
{
	private const ulong UnitType = 0xe0a48d0be9a7453f;
	private const ulong UnitFile = 0x1122334455667788;
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task WriteAsync_RebuildsPatchAndWritesAlignedGpuPayload()
	{
		var sourceDirectory = Path.Combine(root, "source");
		var outputDirectory = Path.Combine(root, "output");
		Directory.CreateDirectory(sourceDirectory);
		var sourcePath = Path.Combine(sourceDirectory, "unit.patch");
		await File.WriteAllBytesAsync(sourcePath, CreateLegacyToc(new byte[] { 1, 2, 3 }));
		var scanner = new PatchTocScanner();
		var entry = (await scanner.ScanEntriesAsync(sourcePath)).Single();
		var original = await new PatchEntryPayloadReader().ReadPayloadAsync(entry);
		var edit = new PatchUnitMeshEditResult(entry, original, new byte[] { 9, 8, 7, 6 }, new byte[] { 5, 4, 3 });

		var result = await new PatchArchiveWriter().WriteAsync(sourcePath, outputDirectory, new[] { edit });

		Assert.True(File.Exists(result.TocFilePath));
		Assert.True(File.Exists(result.GpuResourceFilePath));
		Assert.False(File.Exists(result.StreamFilePath));
		var rebuilt = (await scanner.ScanEntriesAsync(result.TocFilePath)).Single();
		Assert.Equal((uint)1, rebuilt.EntryIndex);
		Assert.Equal((ulong)0, rebuilt.GpuResourceOffset);
		Assert.Equal((uint)3, rebuilt.GpuResourceSize);
		var payload = await new PatchEntryPayloadReader().ReadPayloadAsync(rebuilt);
		Assert.Equal(new byte[] { 9, 8, 7, 6 }, payload.TocData);
		Assert.Equal(new byte[] { 5, 4, 3 }, payload.GpuResourceData);
		Assert.True(new FileInfo(result.TocFilePath).Length >= 256);
	}

	[Fact]
	public async Task WriteAsync_RejectsSourceDirectory()
	{
		Directory.CreateDirectory(root);
		var sourcePath = Path.Combine(root, "unit.patch");
		await File.WriteAllBytesAsync(sourcePath, CreateLegacyToc(new byte[] { 1 }));

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await new PatchArchiveWriter().WriteAsync(sourcePath, root, Array.Empty<PatchUnitMeshEditResult>()));
	}

	[Fact]
	public async Task WriteAsync_RemovesSourceAndAddsFreshEntryWithSameAssetKey()
	{
		var sourceDirectory = Path.Combine(root, "source-same-key");
		var outputDirectory = Path.Combine(root, "output-same-key");
		Directory.CreateDirectory(sourceDirectory);
		var sourcePath = Path.Combine(sourceDirectory, "unit.patch");
		await File.WriteAllBytesAsync(sourcePath, CreateLegacyToc(new byte[] { 1, 2, 3 }));
		var scanner = new PatchTocScanner();
		var entry = (await scanner.ScanEntriesAsync(sourcePath)).Single();
		var addition = new PatchArchiveAdditionalEntry(entry.AssetKey, new byte[] { 7, 7, 7, 7 }, Array.Empty<byte>(), new byte[] { 8, 8 }, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4);

		var result = await new PatchArchiveWriter().WriteAsync(
			sourcePath,
			outputDirectory,
			Array.Empty<PatchUnitMeshEditResult>(),
			new[] { addition },
			new[] { entry },
			preserveOriginalStream: false);

		var rebuilt = (await scanner.ScanEntriesAsync(result.TocFilePath)).Single();
		Assert.Equal(entry.AssetKey, rebuilt.AssetKey);
		var payload = await new PatchEntryPayloadReader().ReadPayloadAsync(rebuilt);
		Assert.Equal(new byte[] { 7, 7, 7, 7 }, payload.TocData);
		Assert.Equal(new byte[] { 8, 8 }, payload.GpuResourceData);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, true);
	}

	private static byte[] CreateLegacyToc(byte[] tocPayload)
	{
		const int typeOffset = 60;
		const int entryOffset = typeOffset + 32;
		const int payloadOffset = entryOffset + 80;
		var data = new byte[payloadOffset + tocPayload.Length];
		Write32(data, 0, 4026531857); Write32(data, 4, 1); Write32(data, 8, 1);
		Write64(data, typeOffset + 8, UnitType); Write64(data, typeOffset + 16, 1);
		Write64(data, entryOffset, UnitFile); Write64(data, entryOffset + 8, UnitType); Write64(data, entryOffset + 16, payloadOffset);
		Write32(data, entryOffset + 56, (uint)tocPayload.Length); Write32(data, entryOffset + 76, 1);
		tocPayload.CopyTo(data, payloadOffset);
		return data;
	}

	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
}