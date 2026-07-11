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
		IReadOnlyCollection<PatchArchiveAdditionalEntry>? additionalEntries = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(patchTocFilePath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(patchTocFilePath));
		}
		ArgumentNullException.ThrowIfNull(unitMeshEdits);
		additionalEntries ??= Array.Empty<PatchArchiveAdditionalEntry>();

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
		var compositeEditsByAsset = BuildCompositeEditMap(patchTocFilePath, unitMeshEdits);
		var removedEntryKeys = BuildRemovedEntrySet(patchTocFilePath, removedEntries);
		var keptEntries = entries.Where(entry => !removedEntryKeys.Contains(CreateEditKey(entry))).ToArray();
		ValidateAdditionalEntries(keptEntries, additionalEntries);
		var entrySources = OrderEntrySourcesByTypeTable(originalTocFileData, layout, BuildEntrySources(patchTocFilePath, keptEntries, additionalEntries));
		var headerData = BuildHeaderData(originalTocFileData, layout, entrySources.Select(source => source.Entry).ToArray());
		entriesOffset = headerData.Length;

		var tocOutput = new MemoryStream(Math.Max(originalTocFileData.Length, checked(entriesOffset + entrySources.Count * EntryRecordSize)));
		tocOutput.Write(headerData, 0, headerData.Length);
		tocOutput.SetLength(checked(entriesOffset + entrySources.Count * EntryRecordSize));
		tocOutput.Position = tocOutput.Length;

		var streamFileData = await ReadOptionalFileAsync(patchTocFilePath + ".stream", cancellationToken).ConfigureAwait(false);
		var streamOutput = new MemoryStream(streamFileData.Length);
		streamOutput.Write(streamFileData, 0, streamFileData.Length);
		var gpuOutput = new MemoryStream();
		var updatedEntries = new List<PatchTocEntry>(entrySources.Count);
		var placements = new List<PatchArchiveEditPlacement>();

		foreach (var source in entrySources)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var entry = source.Entry;
			var key = CreateEditKey(entry);
			var hasUnitEdit = editsByEntry.TryGetValue(key, out var edit);
			var hasCompositeEdit = source.AdditionalEntry is null && !hasUnitEdit && compositeEditsByAsset.TryGetValue(entry.AssetKey, out edit);
			var hasEdit = hasUnitEdit || hasCompositeEdit;
			var payload = source.AdditionalEntry is null
				? hasUnitEdit ? edit!.OriginalPayload : await payloadReader.ReadPayloadAsync(entry, cancellationToken).ConfigureAwait(false)
				: new PatchEntryPayload(entry, source.AdditionalEntry.TocData, source.AdditionalEntry.StreamData, source.AdditionalEntry.GpuResourceData);
			var newTocPayload = source.AdditionalEntry is not null ? source.AdditionalEntry.TocData : hasUnitEdit ? edit!.TocData : hasCompositeEdit ? edit!.CompositeTocData! : payload.TocData;
			var newStreamPayload = source.AdditionalEntry?.StreamData ?? payload.StreamData;
			var newGpuPayload = source.AdditionalEntry is not null ? source.AdditionalEntry.GpuResourceData : hasUnitEdit ? edit!.GpuResourceData : hasCompositeEdit ? edit!.CompositeGpuResourceData! : payload.GpuResourceData;

			var newTocOffset = checked((ulong)tocOutput.Position);
			tocOutput.Write(newTocPayload, 0, newTocPayload.Length);

			var newStreamOffset = entry.StreamOffset;
			if (source.AdditionalEntry is not null && newStreamPayload.Length > 0)
			{
				PadToAlignment(streamOutput, SidecarAlignment);
				newStreamOffset = checked((ulong)streamOutput.Position);
				streamOutput.Write(newStreamPayload, 0, newStreamPayload.Length);
			}

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
				StreamOffset = newStreamOffset,
				GpuResourceOffset = newGpuOffset,
				TocDataSize = checked((uint)newTocPayload.Length),
				StreamSize = checked((uint)newStreamPayload.Length),
				GpuResourceSize = checked((uint)newGpuPayload.Length),
				EntryIndex = checked((uint)updatedEntries.Count + 1U)
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

		PadTocToSdkMinimumSize(tocOutput, entrySources.Count);

		return new PatchArchiveWritePlan(
			patchTocFilePath,
			tocOutput.ToArray(),
			streamOutput.ToArray(),
			gpuOutput.ToArray(),
			updatedEntries,
			placements);
	}

	private static void PadTocToSdkMinimumSize(MemoryStream tocOutput, int entryCount)
	{
		var minimumLength = checked(entryCount * 256);
		if (tocOutput.Length < minimumLength)
		{
			tocOutput.SetLength(minimumLength);
		}
	}

	private static void ValidateAdditionalEntries(IReadOnlyList<PatchTocEntry> keptEntries, IReadOnlyCollection<PatchArchiveAdditionalEntry> additionalEntries)
	{
		var existingKeys = keptEntries.Select(entry => entry.AssetKey).ToHashSet();
		var additionalKeys = new HashSet<AssetKey>();
		foreach (var additionalEntry in additionalEntries)
		{
			ArgumentNullException.ThrowIfNull(additionalEntry);
			if (existingKeys.Contains(additionalEntry.AssetKey))
			{
				throw new InvalidDataException($"Additional entry duplicates existing asset {additionalEntry.AssetKey.TypeId:x16}/{additionalEntry.AssetKey.FileId:x16}.");
			}

			if (!additionalKeys.Add(additionalEntry.AssetKey))
			{
				throw new InvalidDataException($"Duplicate additional entry for asset {additionalEntry.AssetKey.TypeId:x16}/{additionalEntry.AssetKey.FileId:x16}.");
			}
		}
	}

	private static List<EntrySource> BuildEntrySources(
		string patchTocFilePath,
		IReadOnlyList<PatchTocEntry> keptEntries,
		IReadOnlyCollection<PatchArchiveAdditionalEntry> additionalEntries)
	{
		var sources = new List<EntrySource>(checked(keptEntries.Count + additionalEntries.Count));
		foreach (var entry in keptEntries)
		{
			sources.Add(new EntrySource(entry, null));
		}

		foreach (var additionalEntry in additionalEntries)
		{
			var entry = new PatchTocEntry(
				additionalEntry.AssetKey,
				patchTocFilePath,
				Path.GetFileName(patchTocFilePath),
				Unknown1: additionalEntry.Unknown1,
				Unknown2: additionalEntry.Unknown2,
				Unknown3: additionalEntry.Unknown3,
				Unknown4: additionalEntry.Unknown4);
			sources.Add(new EntrySource(entry, additionalEntry));
		}

		return sources;
	}

	private static List<EntrySource> OrderEntrySourcesByTypeTable(byte[] originalTocFileData, TocLayout layout, IReadOnlyList<EntrySource> sources)
	{
		var originalTypeIds = ReadTypeRecords(originalTocFileData, layout).Select(record => record.TypeId).ToArray();
		var knownTypeIds = originalTypeIds.ToHashSet();
		var additionalTypeIds = sources
			.Select(source => source.Entry.AssetKey.TypeId)
			.Where(typeId => !knownTypeIds.Contains(typeId))
			.Distinct()
			.OrderBy(typeId => typeId)
			.ToArray();
		var typeOrder = originalTypeIds.Concat(additionalTypeIds).Select((typeId, index) => new { typeId, index }).ToDictionary(item => item.typeId, item => item.index);

		return sources
			.Select((source, index) => new { source, index })
			.OrderBy(item => typeOrder[item.source.Entry.AssetKey.TypeId])
			.ThenBy(item => item.index)
			.Select(item => item.source)
			.ToList();
	}

	private static byte[] BuildHeaderData(byte[] originalTocFileData, TocLayout layout, IReadOnlyList<PatchTocEntry> entries)
	{
		var originalTypeRecords = ReadTypeRecords(originalTocFileData, layout);
		var originalTypeIds = originalTypeRecords.Select(record => record.TypeId).ToHashSet();
		var newTypeIds = entries
			.Select(entry => entry.AssetKey.TypeId)
			.Where(typeId => !originalTypeIds.Contains(typeId))
			.Distinct()
			.OrderBy(typeId => typeId)
			.ToArray();

		var typeRecords = new List<TypeRecord>(checked(originalTypeRecords.Count + newTypeIds.Length));
		typeRecords.AddRange(originalTypeRecords);
		foreach (var typeId in newTypeIds)
		{
			typeRecords.Add(CreateAdditionalTypeRecord(typeId, originalTypeRecords.FirstOrDefault().Data));
		}

		var headerData = new byte[checked(layout.TypeTableOffset + typeRecords.Count * TypeRecordSize)];
		originalTocFileData.AsSpan(0, layout.TypeTableOffset).CopyTo(headerData);
		WriteUInt32(headerData, 4, checked((uint)typeRecords.Count));
		WriteUInt32(headerData, 8, checked((uint)entries.Count));

		for (var i = 0; i < typeRecords.Count; i++)
		{
			typeRecords[i].Data.CopyTo(headerData.AsSpan(layout.TypeTableOffset + i * TypeRecordSize, TypeRecordSize));
		}

		WriteTypeCounts(headerData, new TocLayout(layout.TypeTableOffset, headerData.Length, checked((uint)typeRecords.Count)), entries);
		return headerData;
	}

	private static List<TypeRecord> ReadTypeRecords(byte[] tocData, TocLayout layout)
	{
		var records = new List<TypeRecord>(checked((int)layout.TypeCount));
		for (var i = 0; i < layout.TypeCount; i++)
		{
			var offset = layout.TypeTableOffset + i * TypeRecordSize;
			var data = tocData.AsSpan(offset, TypeRecordSize).ToArray();
			records.Add(new TypeRecord(BinaryPrimitivesLE.ReadUInt64(data.AsSpan(8, 8)), data));
		}

		return records;
	}

	private static TypeRecord CreateAdditionalTypeRecord(ulong typeId, byte[]? template)
	{
		var data = template is { Length: TypeRecordSize } ? template.ToArray() : new byte[TypeRecordSize];
		WriteUInt64(data, 8, typeId);
		WriteUInt64(data, 16, 0);
		WriteUInt32(data, 24, 16);
		WriteUInt32(data, 28, 64);
		return new TypeRecord(typeId, data);
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

	private static Dictionary<AssetKey, PatchUnitMeshEditResult> BuildCompositeEditMap(string patchTocFilePath, IReadOnlyCollection<PatchUnitMeshEditResult> edits)
	{
		var map = new Dictionary<AssetKey, PatchUnitMeshEditResult>();
		foreach (var edit in edits)
		{
			if (edit.CompositeAssetKey is not { } compositeAssetKey || edit.CompositeTocData is null || edit.CompositeGpuResourceData is null)
			{
				continue;
			}

			if (!Path.GetFullPath(edit.Entry.SourceFilePath).Equals(Path.GetFullPath(patchTocFilePath), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("All patch unit mesh edits must belong to the patch being rebuilt.");
			}

			if (!map.TryAdd(compositeAssetKey, edit))
			{
				throw new InvalidDataException($"Duplicate composite edit for asset {compositeAssetKey.TypeId:x16}/{compositeAssetKey.FileId:x16}.");
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

	private readonly record struct TypeRecord(ulong TypeId, byte[] Data);

	private readonly record struct EntrySource(PatchTocEntry Entry, PatchArchiveAdditionalEntry? AdditionalEntry);
}
