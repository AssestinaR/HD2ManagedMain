using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Binary;

namespace HD2ModCore.Infrastructure;

// Purpose: Compares modded unit resource headers against current game unit structures to flag outdated mods.
public sealed class ModUnitCompatibilityAnalyzer : IModUnitCompatibilityAnalyzer
{
	private const ulong UnitTypeId = 16187218042980615487;
	private const uint ExpectedMagic = 4026531857;
	private const int LegacyHeaderSize = 60;
	private const int StandardHeaderSize = 72;
	private const int TypeRecordSize = 32;
	private const int EntryRecordSize = 80;
	private const uint CurrentUnitLayoutVersion = 0x00A4CD36;

	private readonly IPatchFileNameParser _fileNameParser;

	public ModUnitCompatibilityAnalyzer(IPatchFileNameParser fileNameParser)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
	}

	public async ValueTask<ModUnitCompatibilityReport> AnalyzeNodeAsync(
		ModNode node,
		string modsRootDirectory,
		string? gameDataDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		if (string.IsNullOrWhiteSpace(modsRootDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(modsRootDirectory));
		}

		var nodeDirectory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(nodeDirectory))
		{
			return Empty(node.Id, ModUnitCompatibilityStatus.Unknown);
		}

		var patchFiles = Directory.EnumerateFiles(nodeDirectory, "*", SearchOption.TopDirectoryOnly)
			.Where(path => _fileNameParser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (patchFiles.Count == 0)
		{
			return Empty(node.Id, ModUnitCompatibilityStatus.NoUnitAssets);
		}

		var modUnits = new List<UnitEntryData>();
		var issues = new List<ModUnitCompatibilityIssue>();
		foreach (var patchFile in patchFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var data = await File.ReadAllBytesAsync(patchFile, cancellationToken).ConfigureAwait(false);
				foreach (var entry in ReadTocEntries(data).Where(e => e.TypeId == UnitTypeId))
				{
					if (entry.TocDataOffset > (ulong)data.Length || entry.TocDataSize > data.Length || entry.TocDataOffset + entry.TocDataSize > (ulong)data.Length)
					{
						var invalid = new UnitStructureSummary(false, 0, null, null, null, null, null, false, "unit data range is outside patch file");
						modUnits.Add(new UnitEntryData(entry.FileId, Path.GetFileName(patchFile), invalid, null));
						continue;
					}

					var unitData = data.AsSpan(checked((int)entry.TocDataOffset), checked((int)entry.TocDataSize)).ToArray();
					modUnits.Add(new UnitEntryData(entry.FileId, Path.GetFileName(patchFile), SummarizeUnit(unitData), null));
				}
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException)
			{
				issues.Add(new ModUnitCompatibilityIssue(ModUnitIssueKind.ScanFailed, 0, string.Empty, Path.GetFileName(patchFile), $"扫描 unit 结构失败：{ex.Message}", false, false, null, null));
			}
		}

		if (modUnits.Count == 0)
		{
			return new ModUnitCompatibilityReport(node.Id, issues.Count > 0 ? ModUnitCompatibilityStatus.Unknown : ModUnitCompatibilityStatus.NoUnitAssets, patchFiles.Count, 0, 0, 0, 0, 0, 0, issues);
		}

		var gameUnits = await LoadGameUnitsAsync(gameDataDirectory, modUnits.Select(u => u.FileId).ToHashSet(), cancellationToken).ConfigureAwait(false);
		var invalidCount = 0;
		var oldLayoutCount = 0;
		var versionCount = 0;
		var lodCount = 0;
		var missingCount = 0;

		foreach (var modUnit in modUnits)
		{
			cancellationToken.ThrowIfCancellationRequested();
			gameUnits.TryGetValue(modUnit.FileId, out var gameUnit);

			if (!modUnit.Summary.IsValid)
			{
				invalidCount++;
				issues.Add(BuildIssue(ModUnitIssueKind.InvalidModUnit, modUnit.FileId, modUnit.SourceFileName, modUnit.Summary.Reason ?? "Mod unit 结构无效。", true, gameUnit is not null, modUnit.Summary, gameUnit));
				continue;
			}

			if (modUnit.Summary.IsOldLayout)
			{
				oldLayoutCount++;
				issues.Add(BuildIssue(ModUnitIssueKind.OldLayout, modUnit.FileId, modUnit.SourceFileName, $"Unit 使用旧结构版本 {modUnit.Summary.VersionHex}。", true, gameUnit is not null, modUnit.Summary, gameUnit));
			}

			if (gameUnit is null)
			{
				missingCount++;
				var canRemoveStaleUnit = modUnit.Summary.IsOldLayout;
				var message = canRemoveStaleUnit
					? "当前原版游戏中未找到同 FileID 的旧结构 unit，自动修复会移除该条目。"
					: "当前原版游戏中未找到同 FileID 的 unit。";
				issues.Add(BuildIssue(ModUnitIssueKind.MissingInGame, modUnit.FileId, modUnit.SourceFileName, message, canRemoveStaleUnit, canRemoveStaleUnit, modUnit.Summary, null));
				continue;
			}

			if (!gameUnit.IsValid)
			{
				issues.Add(BuildIssue(ModUnitIssueKind.InvalidGameUnit, modUnit.FileId, modUnit.SourceFileName, gameUnit.Reason ?? "原版 unit 结构无效。", false, false, modUnit.Summary, gameUnit));
				continue;
			}

			if (modUnit.Summary.Version.HasValue && gameUnit.Version.HasValue && modUnit.Summary.Version != gameUnit.Version)
			{
				versionCount++;
				issues.Add(BuildIssue(ModUnitIssueKind.VersionMismatch, modUnit.FileId, modUnit.SourceFileName, $"Unit 版本 {modUnit.Summary.VersionHex} 与当前原版 {gameUnit.VersionHex} 不一致。", true, true, modUnit.Summary, gameUnit));
			}

			if (modUnit.Summary.LodGroupSize.HasValue && gameUnit.LodGroupSize.HasValue && modUnit.Summary.LodGroupSize != gameUnit.LodGroupSize)
			{
				lodCount++;
				issues.Add(BuildIssue(ModUnitIssueKind.LodSizeMismatch, modUnit.FileId, modUnit.SourceFileName, $"LOD group size {modUnit.Summary.LodGroupSize} 与当前原版 {gameUnit.LodGroupSize} 不一致。", false, true, modUnit.Summary, gameUnit));
			}
		}

		var status = invalidCount > 0
			? ModUnitCompatibilityStatus.Invalid
			: (oldLayoutCount > 0 || versionCount > 0 ? ModUnitCompatibilityStatus.Outdated : ModUnitCompatibilityStatus.Current);

		return new ModUnitCompatibilityReport(node.Id, status, patchFiles.Count, modUnits.Count, invalidCount, oldLayoutCount, versionCount, lodCount, missingCount, issues);
	}

	private async ValueTask<IReadOnlyDictionary<ulong, UnitStructureSummary>> LoadGameUnitsAsync(string? gameDataDirectory, IReadOnlySet<ulong> wantedFileIds, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory) || wantedFileIds.Count == 0)
		{
			return new Dictionary<ulong, UnitStructureSummary>();
		}

		var resolver = new GameDataPackageResolver(gameDataDirectory);
		var mapping = await FindGameUnitEntriesAsync(gameDataDirectory, resolver, wantedFileIds, cancellationToken).ConfigureAwait(false);
		var result = new Dictionary<ulong, UnitStructureSummary>();
		foreach (var (fileId, location) in mapping)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var data = await resolver.GetPackageResourceAsync(location.PackageName, location.TocDataOffset, location.TocDataSize, cancellationToken).ConfigureAwait(false);
			if (data is not null)
			{
				result[fileId] = SummarizeUnit(data);
			}
		}

		return result;
	}

	private static async ValueTask<IReadOnlyDictionary<ulong, GameUnitLocation>> FindGameUnitEntriesAsync(string gameDataDirectory, GameDataPackageResolver resolver, IReadOnlySet<ulong> wantedFileIds, CancellationToken cancellationToken)
	{
		var result = new Dictionary<ulong, GameUnitLocation>();
		foreach (var packageName in EnumeratePackageNames(gameDataDirectory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			GameDataPackageToc? toc;
			try
			{
				toc = await resolver.GetPackageTocAsync(packageName, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				continue;
			}

			if (toc is null)
			{
				continue;
			}

			foreach (var entry in ReadTocEntries(toc.Data))
			{
				if (entry.TypeId == UnitTypeId && wantedFileIds.Contains(entry.FileId))
				{
					result[entry.FileId] = new GameUnitLocation(packageName, entry.TocDataOffset, entry.TocDataSize);
				}
			}

			if (wantedFileIds.All(result.ContainsKey))
			{
				break;
			}
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
					if (offset >= data.Length)
					{
						break;
					}

					var length = Math.Min(0x33, data.Length - offset);
					var raw = System.Text.Encoding.UTF8.GetString(data, offset, length);
					var marker = raw.IndexOf('\x17');
					var zero = raw.IndexOf('\0');
					var end = new[] { marker, zero }.Where(x => x >= 0).DefaultIfEmpty(raw.Length).Min();
					var name = raw[..end];
					if (!string.IsNullOrWhiteSpace(name))
					{
						yield return name;
					}
				}
				yield break;
			}
		}

		foreach (var path in Directory.EnumerateFiles(gameDataDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			var name = Path.GetFileName(path);
			if (!name.Contains(".patch", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(name).Length == 0)
			{
				yield return name;
			}
		}
	}

	private static UnitStructureSummary SummarizeUnit(ReadOnlySpan<byte> unitData)
	{
		if (unitData.Length < 0x38)
		{
			return new UnitStructureSummary(false, unitData.Length, null, null, null, null, null, false, "unit data shorter than 0x38");
		}

		var version = BinaryPrimitivesLE.ReadUInt32(unitData.Slice(0x2C, 4));
		var lodGroupOffset = BinaryPrimitivesLE.ReadUInt32(unitData.Slice(0x30, 4));
		var jointListOffset = BinaryPrimitivesLE.ReadUInt32(unitData.Slice(0x34, 4));
		var lodGroupSize = jointListOffset >= lodGroupOffset ? checked((int)(jointListOffset - lodGroupOffset)) : -1;
		var validOffsets = lodGroupOffset <= jointListOffset && jointListOffset <= unitData.Length;
		return new UnitStructureSummary(
			validOffsets,
			unitData.Length,
			version,
			$"0x{version:X8}",
			lodGroupOffset,
			jointListOffset,
			lodGroupSize,
			version < CurrentUnitLayoutVersion,
			validOffsets ? null : "invalid lod/joint offsets");
	}

	private static IReadOnlyList<TocEntryData> ReadTocEntries(ReadOnlySpan<byte> data)
	{
		if (data.Length < 12)
		{
			throw new InvalidDataException("TOC data is too small.");
		}

		var magic = BinaryPrimitivesLE.ReadUInt32(data.Slice(0, 4));
		if (magic != ExpectedMagic)
		{
			throw new InvalidDataException($"Invalid TOC magic: {magic}");
		}

		var numTypes = BinaryPrimitivesLE.ReadUInt32(data.Slice(4, 4));
		var numFiles = BinaryPrimitivesLE.ReadUInt32(data.Slice(8, 4));
		var headerSize = ScoreHeader(data, StandardHeaderSize, numTypes, numFiles) > ScoreHeader(data, LegacyHeaderSize, numTypes, numFiles) ? StandardHeaderSize : LegacyHeaderSize;
		var entriesOffset = checked(headerSize + checked((int)numTypes * TypeRecordSize));
		if (entriesOffset + checked((int)numFiles * EntryRecordSize) > data.Length)
		{
			throw new EndOfStreamException();
		}

		var entries = new List<TocEntryData>(checked((int)Math.Min(numFiles, 1024)));
		for (var i = 0; i < numFiles; i++)
		{
			var offset = entriesOffset + i * EntryRecordSize;
			entries.Add(new TocEntryData(
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 8, 8)),
				BinaryPrimitivesLE.ReadUInt64(data.Slice(offset + 16, 8)),
				BinaryPrimitivesLE.ReadUInt32(data.Slice(offset + 56, 4))));
		}

		return entries;
	}

	private static int ScoreHeader(ReadOnlySpan<byte> data, int headerSize, uint numTypes, uint numFiles)
	{
		var entriesOffset = checked(headerSize + checked((int)numTypes * TypeRecordSize));
		if (entriesOffset + checked((int)numFiles * EntryRecordSize) > data.Length)
		{
			return int.MinValue;
		}

		var typeIds = new HashSet<ulong>();
		var declared = 0UL;
		for (var i = 0; i < numTypes; i++)
		{
			var offset = headerSize + i * TypeRecordSize;
			if (offset + TypeRecordSize > data.Length)
			{
				return int.MinValue;
			}

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

	private static ModUnitCompatibilityReport Empty(ModNodeId nodeId, ModUnitCompatibilityStatus status)
		=> new(nodeId, status, 0, 0, 0, 0, 0, 0, 0, Array.Empty<ModUnitCompatibilityIssue>());

	private static ModUnitCompatibilityIssue BuildIssue(ModUnitIssueKind kind, ulong fileId, string sourceFileName, string message, bool highConfidence, bool repairable, UnitStructureSummary? modUnit, UnitStructureSummary? gameUnit)
		=> new(kind, fileId, $"0x{fileId:X16}", sourceFileName, message, highConfidence, repairable, modUnit, gameUnit);

	private sealed record TocEntryData(ulong FileId, ulong TypeId, ulong TocDataOffset, uint TocDataSize);
	private sealed record UnitEntryData(ulong FileId, string SourceFileName, UnitStructureSummary Summary, UnitStructureSummary? GameSummary);
	private sealed record GameUnitLocation(string PackageName, ulong TocDataOffset, uint TocDataSize);
}