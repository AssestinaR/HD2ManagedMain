using System.Buffers.Binary;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Binary;

namespace HD2ModCore.Infrastructure;

// Purpose: Applies stable and advanced binary unit repair for outdated modded unit resources.
public sealed class ModUnitRepairService : IModUnitRepairService
{
	private const ulong UnitTypeId = 16187218042980615487;
	private const ulong MaterialTypeId = 12445917724151758532;
	private const ulong TextureTypeId = 9505195929401283651;
	private const uint ExpectedMagic = 4026531857;
	private const int LegacyHeaderSize = 60;
	private const int StandardHeaderSize = 72;
	private const int TypeRecordSize = 32;
	private const int EntryRecordSize = 80;
	private const uint CurrentUnitLayoutVersion = 0x00A4CD36;
	private const int ResourceReferenceScanStart = 0x60;
	private const int ResourceReferenceScanLength = 0x300;
	private const int MaxAdvancedReferenceTransplants = 128;
	private const int SidecarAlignment = 64;

	private readonly IPatchFileNameParser _fileNameParser;
	private readonly IModUnitCompatibilityAnalyzer _compatibilityAnalyzer;

	public ModUnitRepairService(IPatchFileNameParser fileNameParser, IModUnitCompatibilityAnalyzer compatibilityAnalyzer)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_compatibilityAnalyzer = compatibilityAnalyzer ?? throw new ArgumentNullException(nameof(compatibilityAnalyzer));
	}

	public async ValueTask<ModUnitRepairResult> RepairNodeAsync(
		ModNode node,
		string modsRootDirectory,
		string gameDataDirectory,
		ModUnitCompatibilityReport? compatibilityReport = null,
		CancellationToken cancellationToken = default)
		=> await RepairNodeCoreAsync(node, modsRootDirectory, gameDataDirectory, RepairMode.Stable, compatibilityReport, cancellationToken).ConfigureAwait(false);

	public async ValueTask<ModUnitRepairResult> RepairNodeAdvancedAsync(
		ModNode node,
		string modsRootDirectory,
		string gameDataDirectory,
		ModUnitCompatibilityReport? compatibilityReport = null,
		CancellationToken cancellationToken = default)
		=> await RepairNodeCoreAsync(node, modsRootDirectory, gameDataDirectory, RepairMode.Advanced, compatibilityReport, cancellationToken).ConfigureAwait(false);

	private async ValueTask<ModUnitRepairResult> RepairNodeCoreAsync(
		ModNode node,
		string modsRootDirectory,
		string gameDataDirectory,
		RepairMode mode,
		ModUnitCompatibilityReport? compatibilityReport = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		if (string.IsNullOrWhiteSpace(modsRootDirectory)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(modsRootDirectory));
		if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
		{
			return Fail(node.Id, "GameDataMissing", "未设置有效的 Helldivers 2 data 目录。", gameDataDirectory);
		}

		compatibilityReport ??= await _compatibilityAnalyzer.AnalyzeNodeAsync(node, modsRootDirectory, gameDataDirectory, cancellationToken).ConfigureAwait(false);
		var repairableIds = compatibilityReport.Issues
			.Where(i => i.IsRepairable && i.Kind is ModUnitIssueKind.OldLayout or ModUnitIssueKind.VersionMismatch or ModUnitIssueKind.LodSizeMismatch or ModUnitIssueKind.InvalidModUnit or ModUnitIssueKind.MissingInGame)
			.Select(i => i.FileId)
			.ToHashSet();
		if (repairableIds.Count == 0)
		{
			return new ModUnitRepairResult(node.Id, true, 0, 0, 0, 0, Array.Empty<CoreIssue>());
		}

		var nodeDirectory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(nodeDirectory))
		{
			return Fail(node.Id, "ModDirectoryMissing", "Mod 目录不存在。", nodeDirectory);
		}

		var patchFiles = Directory.EnumerateFiles(nodeDirectory, "*", SearchOption.TopDirectoryOnly)
			.Where(path => _fileNameParser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var gameUnits = await LoadGameUnitsAsync(gameDataDirectory, repairableIds, cancellationToken).ConfigureAwait(false);
		var issues = new List<CoreIssue>();
		var updatedFiles = 0;
		var updatedUnits = 0;
		var removedUnits = 0;

		foreach (var patchFile in patchFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			PatchRepairResult result;
			try
			{
				result = await RepairPatchFileAsync(patchFile, repairableIds, gameUnits, mode, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "PatchRepairFailed", $"修复 patch 失败：{ex.Message}", patchFile, node.Id, ex.ToString()));
				continue;
			}

			if (result.Issues.Count > 0) issues.AddRange(result.Issues.Select(i => i with { NodeId = node.Id }));
			if (result.Changed) updatedFiles++;
			updatedUnits += result.UpdatedUnitCount;
			removedUnits += result.RemovedUnitCount;
		}

		var success = issues.All(i => i.Severity != CoreIssueSeverity.Error);
		return new ModUnitRepairResult(node.Id, success, patchFiles.Count, updatedFiles, updatedUnits, removedUnits, issues);
	}

	private static async ValueTask<PatchRepairResult> RepairPatchFileAsync(string patchFile, IReadOnlySet<ulong> repairableIds, IReadOnlyDictionary<ulong, GameUnitData> gameUnits, RepairMode mode, CancellationToken cancellationToken)
	{
		var original = await File.ReadAllBytesAsync(patchFile, cancellationToken).ConfigureAwait(false);
		var parsed = ParsePatch(original);
		if (parsed.Entries.Any(e => e.TypeId == UnitTypeId && e.StreamSize > 0))
		{
			return new PatchRepairResult(false, 0, 0, new[]
			{
				new CoreIssue(CoreIssueSeverity.Warning, "ComplexPatchSkipped", "该 patch 的 unit 包含 stream 资源，已跳过自动修复以避免破坏混合资源。", patchFile)
			});
		}

		var sidecarLengths = GetSidecarLengths(patchFile);
		var knownTypeIds = parsed.Types.Select(t => t.TypeId).ToHashSet();
		var kept = new List<TocEntryData>();
		var updated = 0;
		var removed = 0;
		var issues = new List<CoreIssue>();
		var modResourceIds = parsed.Entries
			.Where(e => e.TypeId is not UnitTypeId)
			.Select(e => e.FileId)
			.ToHashSet();

		foreach (var entry in parsed.Entries)
		{
			if (!knownTypeIds.Contains(entry.TypeId))
			{
				removed++;
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "MalformedPatchEntryRemoved", $"移除 Type table 未声明的资源条目 0x{entry.FileId:X16}，TypeID 0x{entry.TypeId:X16}。", patchFile));
				continue;
			}

			if (!IsEntryRangeValid(original.Length, sidecarLengths.GpuResourceLength, sidecarLengths.StreamLength, entry, out var rangeIssue))
			{
				removed++;
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "MalformedPatchEntryRemoved", $"移除越界资源条目 0x{entry.FileId:X16}：{rangeIssue}", patchFile));
				continue;
			}

			if (entry.TypeId != UnitTypeId || !repairableIds.Contains(entry.FileId))
			{
				kept.Add(entry with { Data = ExtractResourceOrEmpty(original, entry) });
				continue;
			}

			if (!gameUnits.TryGetValue(entry.FileId, out var gameUnit))
			{
				removed++;
				continue;
			}

			var data = ExtractResourceOrEmpty(original, entry);
			if (data.Length == 0)
			{
				removed++;
				continue;
			}

			var repaired = mode == RepairMode.Advanced
				? RepairUnitDataAdvanced(data, gameUnit, modResourceIds)
				: RepairUnitData(data, gameUnit);
			kept.Add(entry with { Data = repaired });
			updated++;
		}

		if (updated == 0 && removed == 0)
		{
			return new PatchRepairResult(false, 0, 0, issues);
		}

		var rebuilt = RebuildPatch(original, parsed, kept);
		var backupPath = CreateBackupPath(patchFile);
		File.Copy(patchFile, backupPath, overwrite: false);
		await File.WriteAllBytesAsync(patchFile, rebuilt, cancellationToken).ConfigureAwait(false);
		return new PatchRepairResult(true, updated, removed, issues);
	}

	private static byte[] RepairUnitDataAdvanced(byte[] modUnit, GameUnitData gameUnit, IReadOnlySet<ulong> modResourceIds)
	{
		if (modUnit.Length < 0x38 || gameUnit.Data.Length < 0x38)
		{
			return RepairUnitData(modUnit, gameUnit);
		}

		var result = RepairUnitData(modUnit, gameUnit);
		TransplantResourceReferences(modUnit, result, modResourceIds);
		return result;
	}

	private static SidecarLengths GetSidecarLengths(string patchFile)
	{
		var gpuPath = patchFile + ".gpu_resources";
		var streamPath = patchFile + ".stream";
		var gpuLength = File.Exists(gpuPath) ? new FileInfo(gpuPath).Length : 0L;
		var streamLength = File.Exists(streamPath) ? new FileInfo(streamPath).Length : 0L;
		return new SidecarLengths(gpuLength, streamLength);
	}

	private static bool IsEntryRangeValid(long tocLength, long gpuLength, long streamLength, TocEntryData entry, out string issue)
	{
		if (!IsRangeValid(tocLength, entry.TocDataOffset, entry.TocDataSize))
		{
			issue = $"toc offset {entry.TocDataOffset} size {entry.TocDataSize} 超出 patch 长度 {tocLength}";
			return false;
		}

		if (!IsSidecarRangeValid(gpuLength, entry.GpuResourceOffset, entry.GpuResourceSize))
		{
			issue = $"gpu offset {entry.GpuResourceOffset} size {entry.GpuResourceSize} 超出 gpu_resources 长度 {gpuLength}";
			return false;
		}

		if (!IsSidecarRangeValid(streamLength, entry.StreamFileOffset, entry.StreamSize))
		{
			issue = $"stream offset {entry.StreamFileOffset} size {entry.StreamSize} 超出 stream 长度 {streamLength}";
			return false;
		}

		issue = string.Empty;
		return true;
	}

	private static bool IsRangeValid(long containerLength, ulong offset, uint size)
	{
		if (size == 0) return true;
		if (offset > (ulong)containerLength) return false;
		return offset + size <= (ulong)containerLength;
	}

	private static bool IsSidecarRangeValid(long containerLength, ulong offset, uint size)
	{
		if (size == 0) return true;
		if (containerLength <= 0) return false;
		var alignedLength = AlignUp((ulong)containerLength, SidecarAlignment);
		if (offset > alignedLength) return false;
		return offset + size <= alignedLength;
	}

	private static ulong AlignUp(ulong value, int alignment)
	{
		var mask = checked((ulong)alignment - 1UL);
		return (value + mask) & ~mask;
	}

	private static void TransplantResourceReferences(ReadOnlySpan<byte> modUnit, Span<byte> targetUnit, IReadOnlySet<ulong> modResourceIds)
	{
		if (modResourceIds.Count == 0) return;
		var scanEnd = Math.Min(modUnit.Length, ResourceReferenceScanStart + ResourceReferenceScanLength);
		var transplanted = 0;
		for (var offset = ResourceReferenceScanStart; offset + 8 <= scanEnd && transplanted < MaxAdvancedReferenceTransplants; offset += 4)
		{
			var candidate = BinaryPrimitivesLE.ReadUInt64(modUnit.Slice(offset, 8));
			if (!modResourceIds.Contains(candidate)) continue;
			if (!TryFindAlignedUInt64(targetUnit, ResourceReferenceScanStart, Math.Min(targetUnit.Length, ResourceReferenceScanStart + ResourceReferenceScanLength), offset, out var targetOffset)) continue;

			BinaryPrimitives.WriteUInt64LittleEndian(targetUnit.Slice(targetOffset, 8), candidate);
			transplanted++;
		}
	}

	private static bool TryFindAlignedUInt64(ReadOnlySpan<byte> data, int start, int end, int preferredOffset, out int offset)
	{
		if (preferredOffset + 8 <= data.Length && preferredOffset >= start)
		{
			offset = preferredOffset;
			return true;
		}

		for (var cursor = start; cursor + 8 <= end; cursor += 4)
		{
			var value = BinaryPrimitivesLE.ReadUInt64(data.Slice(cursor, 8));
			if (value != 0)
			{
				offset = cursor;
				return true;
			}
		}

		offset = 0;
		return false;
	}

	private static byte[] RepairUnitData(byte[] modUnit, GameUnitData gameUnit)
	{
		var data = new List<byte>(modUnit);
		if (data.Count < 0x38)
		{
			return data.ToArray();
		}

		var version = ReadUInt32(data, 0x2C);
		if (version < CurrentUnitLayoutVersion)
		{
			AdjustOldLayoutOffsets(data);
		}

		WriteUInt32(data, 0x2C, gameUnit.Version);
		var lodGroupOffset = ReadUInt32(data, 0x30);
		var jointListOffset = ReadUInt32(data, 0x34);
		if (lodGroupOffset > int.MaxValue || jointListOffset > int.MaxValue || jointListOffset < lodGroupOffset || jointListOffset > data.Count)
		{
			return data.ToArray();
		}

		var oldSize = checked((int)(jointListOffset - lodGroupOffset));
		var insertAt = checked((int)lodGroupOffset);
		data.RemoveRange(insertAt, oldSize);
		data.InsertRange(insertAt, gameUnit.LodGroupData);
		var diff = gameUnit.LodGroupData.Length - oldSize;
		if (diff != 0)
		{
			AdjustUnitOffsetsAfter(data, lodGroupOffset, diff);
		}

		return data.ToArray();
	}

	private static void AdjustOldLayoutOffsets(List<byte> data)
	{
		if (data.Count < 0x60) return;
		var layoutListOffset = ReadUInt32(data, 0x5C);
		if (layoutListOffset > int.MaxValue || layoutListOffset + 4 > data.Count) return;
		var baseOffset = checked((int)layoutListOffset);
		var numLayouts = ReadUInt32(data, baseOffset);
		if (numLayouts > 1024 || baseOffset + 4 + numLayouts * 4 > data.Count) return;

		var layoutOffsets = new List<uint>();
		for (var i = 0; i < numLayouts; i++)
		{
			layoutOffsets.Add(ReadUInt32(data, baseOffset + 4 + i * 4));
		}

		foreach (var layoutOffset in layoutOffsets)
		{
			var cursor = baseOffset + checked((int)layoutOffset) + 8;
			for (var i = 0; i < 16; i++)
			{
				if (cursor + 8 > data.Count) break;
				var itemFormat = ReadUInt32(data, cursor + 4);
				if (itemFormat > 16)
				{
					WriteUInt32(data, cursor + 4, itemFormat + 4);
				}
				cursor += 20;
			}
		}
	}

	private static void AdjustUnitOffsetsAfter(List<byte> data, uint lodGroupOffset, int diff)
	{
		var cursor = 0x34;
		for (var i = 0; i < 16; i++)
		{
			if (cursor + 4 > data.Count) break;
			var offset = ReadUInt32(data, cursor);
			if (offset != 0 && offset > lodGroupOffset)
			{
				WriteUInt32(data, cursor, checked((uint)(offset + diff)));
			}
			cursor += 4;
		}
	}

	private static byte[] RebuildPatch(byte[] original, ParsedPatch parsed, IReadOnlyList<TocEntryData> entries)
	{
		var typeCounts = entries.GroupBy(e => e.TypeId).ToDictionary(g => g.Key, g => (ulong)g.Count());
		var headerEnd = parsed.EntriesOffset + parsed.OriginalEntryCount * EntryRecordSize;
		var output = new List<byte>(original.Length);
		output.AddRange(original.AsSpan(0, parsed.EntriesOffset).ToArray());
		WriteUInt32(output, 8, checked((uint)entries.Count));
		foreach (var type in parsed.Types)
		{
			var count = typeCounts.TryGetValue(type.TypeId, out var directCount) ? directCount : 0UL;
			WriteUInt64(output, type.CountOffset, count);
		}

		var dataOffset = checked((ulong)(parsed.EntriesOffset + entries.Count * EntryRecordSize));
		var entryIndex = 1U;
		foreach (var entry in entries)
		{
			AppendEntry(output, entry, dataOffset, entryIndex++);
			dataOffset += checked((ulong)entry.Data.Length);
		}

		foreach (var entry in entries)
		{
			output.AddRange(entry.Data);
		}

		_ = headerEnd;
		return output.ToArray();
	}

	private static void AppendEntry(List<byte> output, TocEntryData entry, ulong dataOffset, uint entryIndex)
	{
		WriteUInt64Append(output, entry.FileId);
		WriteUInt64Append(output, entry.TypeId);
		WriteUInt64Append(output, dataOffset);
		WriteUInt64Append(output, entry.StreamFileOffset);
		WriteUInt64Append(output, entry.GpuResourceOffset);
		WriteUInt64Append(output, entry.Unknown1);
		WriteUInt64Append(output, entry.Unknown2);
		WriteUInt32Append(output, checked((uint)entry.Data.Length));
		WriteUInt32Append(output, entry.StreamSize);
		WriteUInt32Append(output, entry.GpuResourceSize);
		WriteUInt32Append(output, entry.Unknown3);
		WriteUInt32Append(output, entry.Unknown4);
		WriteUInt32Append(output, entryIndex);
	}

	private static byte[] ExtractResourceOrEmpty(byte[] original, TocEntryData entry)
	{
		if (entry.TocDataSize == 0) return Array.Empty<byte>();
		if (entry.TocDataOffset > (ulong)original.Length || entry.TocDataOffset + entry.TocDataSize > (ulong)original.Length) return Array.Empty<byte>();
		return original.AsSpan(checked((int)entry.TocDataOffset), checked((int)entry.TocDataSize)).ToArray();
	}

	private async ValueTask<IReadOnlyDictionary<ulong, GameUnitData>> LoadGameUnitsAsync(string gameDataDirectory, IReadOnlySet<ulong> wantedFileIds, CancellationToken cancellationToken)
	{
		var resolver = new GameDataPackageResolver(gameDataDirectory);
		var mapping = await FindGameUnitEntriesAsync(gameDataDirectory, resolver, wantedFileIds, cancellationToken).ConfigureAwait(false);
		var result = new Dictionary<ulong, GameUnitData>();
		foreach (var (fileId, location) in mapping)
		{
			var data = await resolver.GetPackageResourceAsync(location.PackageName, location.TocDataOffset, location.TocDataSize, cancellationToken).ConfigureAwait(false);
			if (data is null || data.Length < 0x38) continue;
			var version = BinaryPrimitivesLE.ReadUInt32(data.AsSpan(0x2C, 4));
			var lodOffset = BinaryPrimitivesLE.ReadUInt32(data.AsSpan(0x30, 4));
			var jointOffset = BinaryPrimitivesLE.ReadUInt32(data.AsSpan(0x34, 4));
			if (jointOffset < lodOffset || jointOffset > data.Length) continue;
			result[fileId] = new GameUnitData(version, data.AsSpan(checked((int)lodOffset), checked((int)(jointOffset - lodOffset))).ToArray(), data);
		}

		return result;
	}

	private static async ValueTask<IReadOnlyDictionary<ulong, GameUnitLocation>> FindGameUnitEntriesAsync(string gameDataDirectory, GameDataPackageResolver resolver, IReadOnlySet<ulong> wantedFileIds, CancellationToken cancellationToken)
	{
		var result = new Dictionary<ulong, GameUnitLocation>();
		foreach (var packageName in EnumeratePackageNames(gameDataDirectory))
		{
			GameDataPackageToc? toc;
			try
			{
				toc = await resolver.GetPackageTocAsync(packageName, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				continue;
			}

			if (toc is null) continue;
			foreach (var entry in ParsePatch(toc.Data).Entries)
			{
				if (entry.TypeId == UnitTypeId && wantedFileIds.Contains(entry.FileId))
				{
					result[entry.FileId] = new GameUnitLocation(packageName, entry.TocDataOffset, entry.TocDataSize);
				}
			}

			if (wantedFileIds.All(result.ContainsKey)) break;
		}

		return result;
	}

	private static IEnumerable<string> EnumeratePackageNames(string gameDataDirectory)
	{
		var slimDatabase = Path.Combine(gameDataDirectory, "bundle_database.data");
		if (File.Exists(slimDatabase))
		{
			var data = File.ReadAllBytes(slimDatabase);
			if (data.Length >= 8)
			{
				var count = checked((int)BinaryPrimitivesLE.ReadUInt32(data.AsSpan(4, 4)));
				for (var i = 0; i < count; i++)
				{
					var offset = 0x10 + 0x33 * i;
					if (offset >= data.Length) break;
					var raw = System.Text.Encoding.UTF8.GetString(data, offset, Math.Min(0x33, data.Length - offset));
					var marker = raw.IndexOf('\x17');
					var zero = raw.IndexOf('\0');
					var end = new[] { marker, zero }.Where(x => x >= 0).DefaultIfEmpty(raw.Length).Min();
					var name = raw[..end];
					if (!string.IsNullOrWhiteSpace(name)) yield return name;
				}
				yield break;
			}
		}

		foreach (var path in Directory.EnumerateFiles(gameDataDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			var name = Path.GetFileName(path);
			if (!name.Contains(".patch", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(name).Length == 0) yield return name;
		}
	}

	private static ParsedPatch ParsePatch(ReadOnlySpan<byte> data)
	{
		if (data.Length < 12) throw new InvalidDataException("TOC data is too small.");
		var magic = BinaryPrimitivesLE.ReadUInt32(data.Slice(0, 4));
		if (magic != ExpectedMagic) throw new InvalidDataException($"Invalid TOC magic: {magic}");
		var numTypes = BinaryPrimitivesLE.ReadUInt32(data.Slice(4, 4));
		var numFiles = BinaryPrimitivesLE.ReadUInt32(data.Slice(8, 4));
		var headerSize = ScoreHeader(data, StandardHeaderSize, numTypes, numFiles) > ScoreHeader(data, LegacyHeaderSize, numTypes, numFiles) ? StandardHeaderSize : LegacyHeaderSize;
		var entriesOffset = checked(headerSize + checked((int)numTypes * TypeRecordSize));
		if (entriesOffset + checked((int)numFiles * EntryRecordSize) > data.Length) throw new EndOfStreamException();

		var types = new List<TypeEntryData>();
		for (var i = 0; i < numTypes; i++)
		{
			var offset = headerSize + i * TypeRecordSize;
			types.Add(new TypeEntryData(
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 8, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 16, 8)),
				offset + 16));
		}

		var entries = new List<TocEntryData>(checked((int)Math.Min(numFiles, 1024)));
		for (var i = 0; i < numFiles; i++)
		{
			var offset = entriesOffset + i * EntryRecordSize;
			entries.Add(new TocEntryData(
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 8, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 16, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 24, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 32, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 40, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 48, 8)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 56, 4)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 60, 4)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 64, 4)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 68, 4)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 72, 4)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 76, 4)),
				Array.Empty<byte>()));
		}

		return new ParsedPatch(headerSize, entriesOffset, checked((int)numFiles), types, entries);
	}

	private static int ScoreHeader(ReadOnlySpan<byte> data, int headerSize, uint numTypes, uint numFiles)
	{
		var entriesOffset = checked(headerSize + checked((int)numTypes * TypeRecordSize));
		if (entriesOffset + checked((int)numFiles * EntryRecordSize) > data.Length) return int.MinValue;
		var typeIds = new HashSet<ulong>();
		var declared = 0UL;
		for (var i = 0; i < numTypes; i++)
		{
			var offset = headerSize + i * TypeRecordSize;
			if (offset + TypeRecordSize > data.Length) return int.MinValue;
			typeIds.Add(BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 8, 8)));
			declared += BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 16, 8));
		}

		var score = declared == numFiles ? 1000 : 0;
		for (var i = 0; i < numFiles; i++)
		{
			var offset = entriesOffset + i * EntryRecordSize;
			var fileId = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset, 8));
			var typeId = BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 8, 8));
			if (fileId != 0) score++;
			if (typeIds.Contains(typeId)) score += 10;
		}

		return score;
	}

	private static string CreateBackupPath(string patchFile)
	{
		var backup = patchFile + ".bak";
		if (!File.Exists(backup)) return backup;
		return patchFile + $".{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak";
	}

	private static ModUnitRepairResult Fail(ModNodeId nodeId, string code, string message, string? path)
		=> new(nodeId, false, 0, 0, 0, 0, new[] { new CoreIssue(CoreIssueSeverity.Error, code, message, path, nodeId) });

	private static uint ReadUInt32(List<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(CollectionsMarshalAsSpan(data).Slice(offset, 4));
	private static void WriteUInt32(List<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshalAsSpan(data).Slice(offset, 4), value);
	private static void WriteUInt64(List<byte> data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshalAsSpan(data).Slice(offset, 8), value);
	private static void WriteUInt32Append(List<byte> data, uint value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
		data.AddRange(bytes);
	}

	private static void WriteUInt64Append(List<byte> data, ulong value)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
		data.AddRange(bytes);
	}

	private static Span<byte> CollectionsMarshalAsSpan(List<byte> data) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data);

	private sealed record ParsedPatch(int HeaderSize, int EntriesOffset, int OriginalEntryCount, IReadOnlyList<TypeEntryData> Types, IReadOnlyList<TocEntryData> Entries);
	private sealed record TypeEntryData(ulong Key, ulong TypeId, ulong OriginalCount, int CountOffset);
	private sealed record TocEntryData(ulong FileId, ulong TypeId, ulong TocDataOffset, ulong StreamFileOffset, ulong GpuResourceOffset, ulong Unknown1, ulong Unknown2, uint TocDataSize, uint StreamSize, uint GpuResourceSize, uint Unknown3, uint Unknown4, uint EntryIndex, byte[] Data);
	private sealed record SidecarLengths(long GpuResourceLength, long StreamLength);
	private sealed record GameUnitLocation(string PackageName, ulong TocDataOffset, uint TocDataSize);
	private sealed record GameUnitData(uint Version, byte[] LodGroupData, byte[] Data);
	private sealed record PatchRepairResult(bool Changed, int UpdatedUnitCount, int RemovedUnitCount, IReadOnlyList<CoreIssue> Issues);
	private enum RepairMode { Stable, Advanced }
}