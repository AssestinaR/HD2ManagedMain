using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies target-shell archive reconstruction removes stale source topology before adding the current Unit.
public sealed class TargetShellPatchReconstructorTests : IDisposable
{
	private static readonly AssetKey SourceUnitKey = new(PatchUnitMeshReader.UnitTypeId, 0x1111);
	private static readonly AssetKey SourceCompositeKey = new(PatchUnitMeshReader.CompositeUnitTypeId, 0x3333);
	private static readonly AssetKey TargetUnitKey = new(PatchUnitMeshReader.UnitTypeId, 0x2222);
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task WriteAsync_RemovesStaleSourceUnitAndCompositeBeforeAddingTargetUnit()
	{
		var sourceDirectory = Path.Combine(root, "source");
		var outputDirectory = Path.Combine(root, "output");
		Directory.CreateDirectory(sourceDirectory);
		var sourcePath = Path.Combine(sourceDirectory, "unit.patch");
		await File.WriteAllBytesAsync(sourcePath, CreatePatch());
		var output = new TargetShellUnitOutput(
			TargetUnitKey,
			new[] { new PatchArchiveAdditionalEntry(TargetUnitKey, new byte[] { 9, 8, 7 }, Array.Empty<byte>(), new byte[] { 6, 5 }) },
			new[] { SourceUnitKey });

		var result = await new TargetShellPatchReconstructor().WriteAsync(sourcePath, outputDirectory, output);

		var entries = await new PatchTocScanner().ScanEntriesAsync(result.WriteResult.TocFilePath);
		Assert.Equal(TargetUnitKey, Assert.Single(entries).AssetKey);
		Assert.Contains(SourceUnitKey, result.RemovedAssetKeys);
		Assert.Contains(SourceCompositeKey, result.RemovedAssetKeys);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, true);
	}

	private static byte[] CreatePatch()
	{
		const int typeOffset = 60;
		const int entryOffset = typeOffset + 64;
		const int unitPayloadOffset = entryOffset + 160;
		const int compositePayloadOffset = unitPayloadOffset + 24;
		var data = new byte[compositePayloadOffset + 4];
		WriteUInt32(data, 0, 0xf0000011); WriteUInt32(data, 4, 2); WriteUInt32(data, 8, 2);
		WriteUInt64(data, typeOffset + 8, SourceUnitKey.TypeId); WriteUInt64(data, typeOffset + 16, 1);
		WriteUInt64(data, typeOffset + 40, SourceCompositeKey.TypeId); WriteUInt64(data, typeOffset + 48, 1);
		WriteEntry(data, entryOffset, SourceUnitKey, unitPayloadOffset, 24, 1);
		WriteEntry(data, entryOffset + 80, SourceCompositeKey, compositePayloadOffset, 4, 2);
		WriteUInt64(data, unitPayloadOffset + 16, SourceCompositeKey.FileId);
		return data;
	}

	private static void WriteEntry(byte[] data, int offset, AssetKey key, int payloadOffset, uint payloadSize, uint index)
	{
		WriteUInt64(data, offset, key.FileId); WriteUInt64(data, offset + 8, key.TypeId); WriteUInt64(data, offset + 16, (ulong)payloadOffset);
		WriteUInt32(data, offset + 56, payloadSize); WriteUInt32(data, offset + 76, index);
	}

	private static void WriteUInt32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void WriteUInt64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
}