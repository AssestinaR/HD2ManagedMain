using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Binary;

namespace HD2ModCore.Infrastructure;

// 作用：快速扫描 HD2 的 .patch_n（TOC）并提取 (TypeID, FileID) 资产键，不解析资源体。
// Purpose: Fast scanner that reads an HD2 .patch_n TOC and extracts (TypeID, FileID) asset keys without decoding resource data.
public sealed class PatchTocScanner : IPatchTocScanner
{
	private const uint ExpectedMagic = 4026531857;
	private const int TocHeaderSizeToTypesEnd = 60;
	private const int SlimTocHeaderSizeToTypesEnd = 72;
	private const int TypeRecordSize = 32;
	private const int EntryRecordSize = 80;

	public async ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
	{
		var entries = await ScanEntriesAsync(patchTocFilePath, cancellationToken).ConfigureAwait(false);
		return entries.Select(e => e.AssetKey).ToHashSet();
	}

	public IReadOnlySet<AssetKey> ScanAssetKeys(ReadOnlySpan<byte> tocData, bool usesSlimEntryOffset = false)
	{
		if (tocData.Length < 12)
		{
			throw new InvalidDataException("TOC data is too small.");
		}

		var magic = BinaryPrimitivesLE.ReadUInt32(tocData.Slice(0, 4));
		if (magic != ExpectedMagic)
		{
			throw new InvalidDataException($"Invalid TOC magic: {magic}");
		}

		var numTypes = BinaryPrimitivesLE.ReadUInt32(tocData.Slice(4, 4));
		var numFiles = BinaryPrimitivesLE.ReadUInt32(tocData.Slice(8, 4));
		var entriesOffset = ResolveEntriesOffset(tocData, numTypes, numFiles, usesSlimEntryOffset);
		var bytesNeeded = checked(entriesOffset + checked((int)numFiles * EntryRecordSize));
		if (tocData.Length < bytesNeeded)
		{
			throw new EndOfStreamException();
		}

		var result = new HashSet<AssetKey>();
		var offset = entriesOffset;
		for (var i = 0; i < numFiles; i++)
		{
			var fileId = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(offset, 8));
			var typeId = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(offset + 8, 8));
			result.Add(new AssetKey(typeId, fileId));
			offset += EntryRecordSize;
		}

		return result;
	}

	public IReadOnlyList<PatchTocEntry> ScanEntries(ReadOnlySpan<byte> tocData, string sourceFilePath, bool usesSlimEntryOffset = false)
	{
		if (tocData.Length < 12)
		{
			throw new InvalidDataException("TOC data is too small.");
		}

		var magic = BinaryPrimitivesLE.ReadUInt32(tocData.Slice(0, 4));
		if (magic != ExpectedMagic)
		{
			throw new InvalidDataException($"Invalid TOC magic: {magic}");
		}

		var numTypes = BinaryPrimitivesLE.ReadUInt32(tocData.Slice(4, 4));
		var numFiles = BinaryPrimitivesLE.ReadUInt32(tocData.Slice(8, 4));
		var entriesOffset = ResolveEntriesOffset(tocData, numTypes, numFiles, usesSlimEntryOffset);
		var bytesNeeded = checked(entriesOffset + checked((int)numFiles * EntryRecordSize));
		if (tocData.Length < bytesNeeded)
		{
			throw new EndOfStreamException();
		}

		var result = new List<PatchTocEntry>((int)Math.Min(numFiles, 1024u));
		var sourceFileName = Path.GetFileName(sourceFilePath);
		var offset = entriesOffset;
		for (var i = 0; i < numFiles; i++)
		{
			result.Add(ReadEntry(tocData, offset, sourceFilePath, sourceFileName));
			offset += EntryRecordSize;
		}

		return result;
	}

	public async ValueTask<IReadOnlyList<PatchTocEntry>> ScanEntriesAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(patchTocFilePath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(patchTocFilePath));
		}

		await using var fs = new FileStream(
			patchTocFilePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			bufferSize: 64 * 1024,
			options: FileOptions.Asynchronous | FileOptions.SequentialScan);

		var header = new byte[12];
		await ReadExactAsync(fs, header, cancellationToken).ConfigureAwait(false);

		var magic = BinaryPrimitivesLE.ReadUInt32(header.AsSpan(0, 4));
		if (magic != ExpectedMagic)
		{
			throw new InvalidDataException($"Invalid TOC magic in '{patchTocFilePath}': {magic}");
		}

		var numTypes = BinaryPrimitivesLE.ReadUInt32(header.AsSpan(4, 4));
		var numFiles = BinaryPrimitivesLE.ReadUInt32(header.AsSpan(8, 4));

		var probeSize = checked(SlimTocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize) + checked((int)numFiles * EntryRecordSize));
		var legacySize = checked(TocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize) + checked((int)numFiles * EntryRecordSize));
		var bytesToRead = Math.Min((int)fs.Length, Math.Max(probeSize, legacySize));

		fs.Position = 0;
		var buffer = new byte[bytesToRead];
		await ReadExactAsync(fs, buffer, cancellationToken).ConfigureAwait(false);

		var entriesOffset = ResolveEntriesOffset(buffer, numTypes, numFiles, usesSlimEntryOffset: false);
		var bytesNeeded = checked(entriesOffset + checked((int)numFiles * EntryRecordSize));
		if (buffer.Length < bytesNeeded)
		{
			throw new EndOfStreamException();
		}

		return ScanEntries(buffer, patchTocFilePath, usesSlimEntryOffset: false);
	}

	private static PatchTocEntry ReadEntry(ReadOnlySpan<byte> data, int offset, string sourceFilePath, string sourceFileName)
	{
		var fileId = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset, 8));
		var typeId = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 8, 8));
		var tocDataOffset = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 16, 8));
		var streamOffset = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 24, 8));
		var gpuResourceOffset = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 32, 8));
		var unknown1 = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 40, 8));
		var unknown2 = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 48, 8));
		var tocDataSize = BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 56, 4));
		var streamSize = BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 60, 4));
		var gpuResourceSize = BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 64, 4));
		var unknown3 = BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 68, 4));
		var unknown4 = BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 72, 4));
		var entryIndex = BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 76, 4));
		return new PatchTocEntry(
			new AssetKey(typeId, fileId),
			sourceFilePath,
			sourceFileName,
			tocDataOffset,
			streamOffset,
			gpuResourceOffset,
			unknown1,
			unknown2,
			tocDataSize,
			streamSize,
			gpuResourceSize,
			unknown3,
			unknown4,
			entryIndex);
	}

	private static int ResolveEntriesOffset(ReadOnlySpan<byte> tocData, uint numTypes, uint numFiles, bool usesSlimEntryOffset)
	{
		if (usesSlimEntryOffset)
		{
			return checked(SlimTocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize));
		}

		var legacyOffset = checked(TocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize));
		var standardOffset = checked(SlimTocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize));
		if (numTypes == 0)
		{
			return legacyOffset;
		}

		var legacyScore = ScoreEntryOffsetCandidate(tocData, TocHeaderSizeToTypesEnd, legacyOffset, numTypes, numFiles);
		var standardScore = ScoreEntryOffsetCandidate(tocData, SlimTocHeaderSizeToTypesEnd, standardOffset, numTypes, numFiles);
		return standardScore > legacyScore ? standardOffset : legacyOffset;
	}

	private static int ScoreEntryOffsetCandidate(ReadOnlySpan<byte> tocData, int typeTableOffset, int entriesOffset, uint numTypes, uint numFiles)
	{
		var bytesNeeded = checked(entriesOffset + checked((int)numFiles * EntryRecordSize));
		if (tocData.Length < bytesNeeded)
		{
			return int.MinValue;
		}

		var typeIds = new HashSet<ulong>();
		var declaredFileCount = 0UL;
		for (var i = 0; i < numTypes; i++)
		{
			var offset = typeTableOffset + i * TypeRecordSize;
			if (tocData.Length < offset + TypeRecordSize)
			{
				return int.MinValue;
			}

			var typeId = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(offset + 8, 8));
			var typeFileCount = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(offset + 16, 8));
			typeIds.Add(typeId);
			declaredFileCount += typeFileCount;
		}

		var score = declaredFileCount == numFiles ? 1000 : 0;
		var entryOffset = entriesOffset;
		for (var i = 0; i < numFiles; i++)
		{
			var fileId = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(entryOffset, 8));
			var typeId = BinaryPrimitivesLE.ReadUInt64(tocData.Slice(entryOffset + 8, 8));
			if (fileId != 0)
			{
				score++;
			}
			if (typeIds.Contains(typeId))
			{
				score += 10;
			}
			entryOffset += EntryRecordSize;
		}

		return score;
	}

	private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		var totalRead = 0;
		while (totalRead < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				throw new EndOfStreamException();
			}
			totalRead += read;
		}
	}
}
