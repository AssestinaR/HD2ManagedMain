using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Binary;

namespace HD2ModCore.Infrastructure;

// 作用：重建 patch archive 的 dry-run 输出，更新 entry offset/size 但不写入磁盘。
// Purpose: Builds dry-run patch archive output with updated entry offsets/sizes without writing to disk.
public sealed class PatchArchiveDryWriter : IPatchArchiveDryWriter
{
	private const uint ExpectedMagic = 4026531857;
	private const int TocHeaderSizeToTypesEnd = 60;
	private const int SlimTocHeaderSizeToTypesEnd = 72;
	private const int TypeRecordSize = 32;
	private const int EntryRecordSize = 80;
	private const int SidecarAlignment = 64;

	private readonly IPatchTocScanner tocScanner;
	private readonly IPatchEntryPayloadReader payloadReader;

	public PatchArchiveDryWriter(IPatchTocScanner tocScanner, IPatchEntryPayloadReader payloadReader)
	{
		this.tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
		this.payloadReader = payloadReader ?? throw new ArgumentNullException(nameof(payloadReader));
	}

	public async ValueTask<PatchArchiveWritePlan> BuildWritePlanAsync(
		string patchTocFilePath,
		IReadOnlyCollection<PatchUnitMeshEditResult> unitMeshEdits,
		IReadOnlyCollection<PatchTocEntry>? removedEntries = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(patchTocFilePath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(patchTocFilePath));
		}
		ArgumentNullException.ThrowIfNull(unitMeshEdits);

		var originalTocFileData = await File.ReadAllBytesAsync(patchTocFilePath, cancellationToken).ConfigureAwait(false);
		var layout = ResolveTocLayout(originalTocFileData);
		var entriesOffset = layout.EntriesOffset;
		var entries = await tocScanner.ScanEntriesAsync(patchTocFilePath, cancellationToken).ConfigureAwait(false);
		var entryTableEnd = checked(entriesOffset + checked(entries.Count * EntryRecordSize));
		if (originalTocFileData.Length < entryTableEnd)
		{
			throw new InvalidDataException("Patch TOC entry table is truncated.");
		}

		var editsByEntry = BuildEditMap(patchTocFilePath, unitMeshEdits);
		var removedEntryKeys = BuildRemovedEntrySet(patchTocFilePath, removedEntries);
		var keptEntries = entries.Where(entry => !removedEntryKeys.Contains(CreateEditKey(entry))).ToArray();
		var headerData = originalTocFileData.AsSpan(0, entriesOffset).ToArray();
		WriteUInt32(headerData, 8, checked((uint)keptEntries.Length));
		WriteTypeCounts(headerData, layout, keptEntries);

		var tocOutput = new MemoryStream(Math.Max(originalTocFileData.Length, checked(entriesOffset + keptEntries.Length * EntryRecordSize)));
		tocOutput.Write(headerData, 0, headerData.Length);
		tocOutput.SetLength(checked(entriesOffset + keptEntries.Length * EntryRecordSize));
		tocOutput.Position = tocOutput.Length;

		var gpuOutput = new MemoryStream();
		var updatedEntries = new List<PatchTocEntry>(keptEntries.Length);
		var placements = new List<PatchArchiveEditPlacement>();

		foreach (var entry in keptEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var key = CreateEditKey(entry);
			var hasEdit = editsByEntry.TryGetValue(key, out var edit);
			var payload = hasEdit ? edit!.OriginalPayload : await payloadReader.ReadPayloadAsync(entry, cancellationToken).ConfigureAwait(false);
			var newTocPayload = hasEdit ? edit!.TocData : payload.TocData;
			var newGpuPayload = hasEdit ? edit!.GpuResourceData : payload.GpuResourceData;

			var newTocOffset = checked((ulong)tocOutput.Position);
			tocOutput.Write(newTocPayload, 0, newTocPayload.Length);

			var newGpuOffset = 0UL;
			if (newGpuPayload.Length > 0)
			{
				PadToAlignment(gpuOutput, SidecarAlignment);
				newGpuOffset = checked((ulong)gpuOutput.Position);
				gpuOutput.Write(newGpuPayload, 0, newGpuPayload.Length);
			}

			var updatedEntry = entry with
			{
				TocDataOffset = newTocOffset,
				GpuResourceOffset = newGpuOffset,
				TocDataSize = checked((uint)newTocPayload.Length),
				GpuResourceSize = checked((uint)newGpuPayload.Length),
				EntryIndex = checked((uint)updatedEntries.Count)
			};
			updatedEntries.Add(updatedEntry);
			WriteEntryRecord(tocOutput.GetBuffer(), entriesOffset, updatedEntries.Count - 1, updatedEntry);

			if (hasEdit)
			{
				placements.Add(new PatchArchiveEditPlacement(
					entry,
					updatedEntry,
					newTocPayload.Length - payload.TocData.Length,
					newGpuPayload.Length - payload.GpuResourceData.Length));
			}
		}

		var streamFileData = await ReadOptionalFileAsync(patchTocFilePath + ".stream", cancellationToken).ConfigureAwait(false);
		return new PatchArchiveWritePlan(
			patchTocFilePath,
			tocOutput.ToArray(),
			streamFileData,
			gpuOutput.ToArray(),
			updatedEntries,
			placements);
	}

	private static Dictionary<EditKey, PatchUnitMeshEditResult> BuildEditMap(string patchTocFilePath, IReadOnlyCollection<PatchUnitMeshEditResult> edits)
	{
		var map = new Dictionary<EditKey, PatchUnitMeshEditResult>();
		foreach (var edit in edits)
		{
			if (!Path.GetFullPath(edit.Entry.SourceFilePath).Equals(Path.GetFullPath(patchTocFilePath), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("All patch unit mesh edits must belong to the patch being rebuilt.");
			}

			var key = CreateEditKey(edit.Entry);
			if (!map.TryAdd(key, edit))
			{
				throw new InvalidDataException($"Duplicate edit for entry index {edit.Entry.EntryIndex}.");
			}
		}

		return map;
	}

	private static HashSet<EditKey> BuildRemovedEntrySet(string patchTocFilePath, IReadOnlyCollection<PatchTocEntry>? removedEntries)
	{
		var set = new HashSet<EditKey>();
		if (removedEntries is null)
		{
			return set;
		}

		foreach (var entry in removedEntries)
		{
			if (!Path.GetFullPath(entry.SourceFilePath).Equals(Path.GetFullPath(patchTocFilePath), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("All removed patch entries must belong to the patch being rebuilt.");
			}

			set.Add(CreateEditKey(entry));
		}

		return set;
	}

	private static EditKey CreateEditKey(PatchTocEntry entry)
		=> new(entry.EntryIndex, entry.AssetKey.TypeId, entry.AssetKey.FileId);

	private static TocLayout ResolveTocLayout(ReadOnlySpan<byte> tocData)
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
		var legacyOffset = checked(TocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize));
		var standardOffset = checked(SlimTocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize));
		if (numTypes == 0)
		{
			return new TocLayout(TocHeaderSizeToTypesEnd, legacyOffset, numTypes);
		}

		var legacyScore = ScoreEntryOffsetCandidate(tocData, TocHeaderSizeToTypesEnd, legacyOffset, numTypes, numFiles);
		var standardScore = ScoreEntryOffsetCandidate(tocData, SlimTocHeaderSizeToTypesEnd, standardOffset, numTypes, numFiles);
		return standardScore > legacyScore
			? new TocLayout(SlimTocHeaderSizeToTypesEnd, standardOffset, numTypes)
			: new TocLayout(TocHeaderSizeToTypesEnd, legacyOffset, numTypes);
	}

	private static void WriteTypeCounts(byte[] tocData, TocLayout layout, IReadOnlyList<PatchTocEntry> entries)
	{
		var counts = entries.GroupBy(entry => entry.AssetKey.TypeId).ToDictionary(group => group.Key, group => checked((ulong)group.Count()));
		for (var i = 0; i < layout.TypeCount; i++)
		{
			var offset = layout.TypeTableOffset + i * TypeRecordSize;
			var typeId = BinaryPrimitivesLE.ReadUInt64(tocData.AsSpan(offset + 8, 8));
			counts.TryGetValue(typeId, out var count);
			WriteUInt64(tocData, offset + 16, count);
		}
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

	private static void WriteEntryRecord(byte[] tocData, int entriesOffset, int entryOrdinal, PatchTocEntry entry)
	{
		var offset = entriesOffset + entryOrdinal * EntryRecordSize;
		WriteUInt64(tocData, offset, entry.AssetKey.FileId);
		WriteUInt64(tocData, offset + 8, entry.AssetKey.TypeId);
		WriteUInt64(tocData, offset + 16, entry.TocDataOffset);
		WriteUInt64(tocData, offset + 24, entry.StreamOffset);
		WriteUInt64(tocData, offset + 32, entry.GpuResourceOffset);
		WriteUInt64(tocData, offset + 40, entry.Unknown1);
		WriteUInt64(tocData, offset + 48, entry.Unknown2);
		WriteUInt32(tocData, offset + 56, entry.TocDataSize);
		WriteUInt32(tocData, offset + 60, entry.StreamSize);
		WriteUInt32(tocData, offset + 64, entry.GpuResourceSize);
		WriteUInt32(tocData, offset + 68, entry.Unknown3);
		WriteUInt32(tocData, offset + 72, entry.Unknown4);
		WriteUInt32(tocData, offset + 76, entry.EntryIndex);
	}

	private static void PadToAlignment(Stream stream, int alignment)
	{
		var padding = (alignment - (int)(stream.Position % alignment)) % alignment;
		for (var i = 0; i < padding; i++)
		{
			stream.WriteByte(0);
		}
	}

	private static async ValueTask<byte[]> ReadOptionalFileAsync(string path, CancellationToken cancellationToken)
		=> File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : Array.Empty<byte>();

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

	private readonly record struct EditKey(uint EntryIndex, ulong TypeId, ulong FileId);

	private readonly record struct TocLayout(int TypeTableOffset, int EntriesOffset, uint TypeCount);
}
