using HD2ModCore.Application;
using HD2ModCore.Domain;
using System.Text.RegularExpressions;

namespace HD2ModCore.Infrastructure;

// Purpose: Scans one mod node and produces readable asset entries with derived asset tags.
public sealed class ModAssetAnalyzer : IModAssetAnalyzer
{
	private static readonly Regex EquipmentCodePattern = new(@"\b[a-z]{1,4}-\d{1,4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex HexIdPattern = new(@"^0x[0-9a-f]{8,16}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex HashLikePattern = new(@"^[0-9a-f]{12,16}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private readonly IPatchFileNameParser _fileNameParser;
	private readonly IPatchTocScanner _tocScanner;
	private readonly IAssetMetadataCatalogProvider _catalogProvider;
	private readonly IAssetArchiveIndexService? _archiveIndexService;

	public ModAssetAnalyzer(
		IPatchFileNameParser fileNameParser,
		IPatchTocScanner tocScanner,
		IAssetMetadataCatalogProvider catalogProvider,
		IAssetArchiveIndexService? archiveIndexService = null)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
		_catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
		_archiveIndexService = archiveIndexService;
	}

	public async ValueTask<ModAssetSummary> AnalyzeNodeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		if (node is null)
		{
			throw new ArgumentNullException(nameof(node));
		}
		if (string.IsNullOrWhiteSpace(modsRootDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(modsRootDirectory));
		}

		var catalog = await _catalogProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
		var rawEntries = new Dictionary<PatchAssetKey, List<string>>();
		var nodeDir = Path.Combine(modsRootDirectory, node.RelativePath);
		if (Directory.Exists(nodeDir))
		{
			foreach (var patchFile in Directory.EnumerateFiles(nodeDir, "*", SearchOption.TopDirectoryOnly))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var fileName = Path.GetFileName(patchFile);
				if (!_fileNameParser.TryParse(fileName, out var parsedInfo) || parsedInfo is null || parsedInfo.SidecarKind != PatchSidecarKind.Base)
				{
					continue;
				}

				IReadOnlyList<PatchTocEntry> entries;
				try
				{
					entries = await _tocScanner.ScanEntriesAsync(patchFile, cancellationToken).ConfigureAwait(false);
				}
				catch
				{
					continue;
				}

				foreach (var entry in entries)
				{
					var key = new PatchAssetKey(parsedInfo.ArchiveHex16.ToLowerInvariant(), entry.AssetKey.TypeId, entry.AssetKey.FileId);
					if (!rawEntries.TryGetValue(key, out var sources))
					{
						sources = new List<string>();
						rawEntries[key] = sources;
					}
					if (!sources.Contains(entry.SourceFileName, StringComparer.OrdinalIgnoreCase))
					{
						sources.Add(entry.SourceFileName);
					}
				}
			}
		}

		var indexedArchiveMatches = await LoadIndexedArchiveMatchesAsync(rawEntries.Keys, cancellationToken).ConfigureAwait(false);
		var assets = rawEntries
			.OrderBy(x => x.Key.ArchiveId, StringComparer.OrdinalIgnoreCase)
			.ThenBy(x => x.Key.TypeId)
			.ThenBy(x => x.Key.FileId)
			.Select(x => BuildEntry(x.Key, x.Value, catalog, indexedArchiveMatches))
			.ToList();
		var tags = BuildSummaryTags(assets);
		var targetGroups = BuildTargetGroups(assets);

		return new ModAssetSummary(node.Id, node.Metadata.Name, assets, tags, targetGroups);
	}

	private async ValueTask<IReadOnlyDictionary<AssetKey, IReadOnlyList<ArchiveMetadata>>> LoadIndexedArchiveMatchesAsync(
		IEnumerable<PatchAssetKey> patchAssetKeys,
		CancellationToken cancellationToken)
	{
		if (_archiveIndexService is null)
		{
			return new Dictionary<AssetKey, IReadOnlyList<ArchiveMetadata>>();
		}

		var assetKeys = patchAssetKeys.Select(x => x.AssetKey).ToHashSet();
		if (assetKeys.Count == 0)
		{
			return new Dictionary<AssetKey, IReadOnlyList<ArchiveMetadata>>();
		}

		try
		{
			var matches = await _archiveIndexService.FindAssetArchivesAsync(assetKeys, cancellationToken).ConfigureAwait(false);
			return matches
				.Where(x => x.Found)
				.ToDictionary(x => x.AssetKey, x => x.Archives, EqualityComparer<AssetKey>.Default);
		}
		catch
		{
			return new Dictionary<AssetKey, IReadOnlyList<ArchiveMetadata>>();
		}
	}

	private static PatchAssetEntry BuildEntry(
		PatchAssetKey key,
		IReadOnlyList<string> sourceFiles,
		AssetMetadataCatalog catalog,
		IReadOnlyDictionary<AssetKey, IReadOnlyList<ArchiveMetadata>> indexedArchiveMatches)
	{
		var archive = catalog.FindArchive(key.ArchiveId);
		var file = catalog.FindFile(key.FileId);
		var type = catalog.FindType(key.TypeId);
		indexedArchiveMatches.TryGetValue(key.AssetKey, out var replacementTargets);
		var typeCategory = type?.Category ?? AssetTypeCategory.Unknown;
		var semanticArchive = PickSemanticArchive(archive, replacementTargets);
		var archiveDisplayName = semanticArchive?.DisplayName ?? archive?.DisplayName ?? key.ArchiveId;
		var archiveCategory = semanticArchive?.Category ?? archive?.Category ?? "Unknown";
		var archiveCategoryOrder = semanticArchive?.CategoryOrder ?? archive?.CategoryOrder ?? int.MaxValue;
		var archiveOrder = semanticArchive?.ArchiveOrder ?? archive?.ArchiveOrder ?? int.MaxValue;
		var fileDisplayName = file?.FriendlyName ?? key.FileId.ToString();
		var typeDisplayName = type?.Name ?? $"0x{key.TypeId:x16}";
		var semanticArchives = replacementTargets ?? Array.Empty<ArchiveMetadata>();

		return new PatchAssetEntry(
			key,
			archiveDisplayName,
			archiveCategory,
			archiveCategoryOrder,
			archiveOrder,
			fileDisplayName,
			typeDisplayName,
			typeCategory,
			BuildEntryTags(archiveCategory, archiveDisplayName, fileDisplayName, typeDisplayName, typeCategory, semanticArchives),
			sourceFiles.Order(StringComparer.OrdinalIgnoreCase).ToList());
	}

	private static ArchiveMetadata? PickSemanticArchive(ArchiveMetadata? patchArchive, IReadOnlyList<ArchiveMetadata>? replacementTargets)
	{
		if (replacementTargets is null || replacementTargets.Count == 0)
		{
			return patchArchive;
		}

		var semantic = replacementTargets.FirstOrDefault(x => !string.Equals(x.Category, "Unknown", StringComparison.OrdinalIgnoreCase));
		return semantic ?? replacementTargets[0];
	}

	private static IReadOnlyList<string> BuildEntryTags(
		string archiveCategory,
		string archiveDisplayName,
		string fileDisplayName,
		string typeDisplayName,
		AssetTypeCategory typeCategory,
		IReadOnlyList<ArchiveMetadata> semanticArchives)
	{
		var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		AddNormalized(tags, archiveCategory);
		if (typeCategory != AssetTypeCategory.Unknown)
		{
			AddNormalized(tags, typeCategory.ToString());
		}
		AddSemanticTypeTag(tags, typeDisplayName);
		AddArchiveDisplayTag(tags, archiveDisplayName);
		foreach (var archive in semanticArchives)
		{
			AddNormalized(tags, archive.Category);
			AddArchiveDisplayTag(tags, archive.DisplayName);
		}

		var searchable = $"{archiveCategory} {archiveDisplayName} {fileDisplayName} {typeDisplayName}";

		var lower = searchable.ToLowerInvariant();
		foreach (var hint in new[] { "armor", "armour", "helmet", "cape", "weapon", "audio", "sound", "sfx", "voice", "texture", "material", "enemy", "stratagem", "vehicle", "objective", "player", "unit", "prefab", "renderable" })
		{
			if (lower.Contains(hint, StringComparison.OrdinalIgnoreCase))
			{
				AddKnownAssetWord(tags, hint);
			}
		}

		return tags.ToList();
	}

	private static IReadOnlyList<ModAssetTargetGroup> BuildTargetGroups(IReadOnlyList<PatchAssetEntry> assets)
	{
		return assets
			.Where(asset => !string.Equals(asset.ArchiveCategory, "Unknown", StringComparison.OrdinalIgnoreCase))
			.GroupBy(asset => new { asset.ArchiveCategory, asset.ArchiveCategoryOrder })
			.OrderBy(group => group.Key.ArchiveCategoryOrder)
			.ThenBy(group => group.Key.ArchiveCategory, StringComparer.OrdinalIgnoreCase)
			.Select(group => new ModAssetTargetGroup(
				group.Key.ArchiveCategory,
				group.Key.ArchiveCategoryOrder,
				BuildTargetItems(group),
				group.Count()))
			.ToList();
	}

	private static IReadOnlyList<ModAssetTargetItem> BuildTargetItems(IEnumerable<PatchAssetEntry> assets)
	{
		return assets
			.GroupBy(asset => new { asset.ArchiveDisplayName, asset.ArchiveOrder })
			.OrderBy(group => group.Key.ArchiveOrder)
			.ThenBy(group => group.Key.ArchiveDisplayName, StringComparer.OrdinalIgnoreCase)
			.Select(group => new ModAssetTargetItem(
				group.Key.ArchiveDisplayName,
				group.Key.ArchiveOrder,
				group.Select(asset => asset.Key.ArchiveId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
				group.Select(asset => asset.TypeDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
				group.Count()))
			.ToList();
	}

	private static void AddArchiveDisplayTag(ISet<string> tags, string displayName)
	{
		var normalized = displayName.Trim();
		if (normalized.Length == 0 || LooksLikeInternalId(normalized) || string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		tags.Add(normalized);
	}

	private static void AddSemanticTypeTag(ISet<string> tags, string typeDisplayName)
	{
		if (LooksLikeInternalId(typeDisplayName))
		{
			return;
		}

		AddKnownAssetWord(tags, typeDisplayName);
	}

	private static void AddKnownAssetWord(ISet<string> tags, string value)
	{
		var normalized = value.Trim().ToLowerInvariant();
		if (LooksLikeInternalId(normalized))
		{
			return;
		}

		if (normalized is "armour")
		{
			normalized = "armor";
		}

		if (normalized is "characters" or "character")
		{
			normalized = "character";
		}

		if (normalized is "unit" or "prefab" or "renderable")
		{
			normalized = "model";
		}

		if (normalized is "sound" or "sfx" or "voice")
		{
			normalized = "audio";
		}

		AddNormalized(tags, normalized);
	}

	private static IReadOnlyList<string> BuildSummaryTags(IReadOnlyList<PatchAssetEntry> assets)
	{
		var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var asset in assets)
		{
			foreach (var tag in asset.DerivedTags)
			{
				tags.Add(tag);
			}
		}
		return tags.ToList();
	}

	private static void AddNormalized(ISet<string> tags, string value)
	{
		var normalized = value.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
		if (normalized.Length > 0 && normalized != "unknown" && !LooksLikeInternalId(normalized))
		{
			tags.Add(normalized);
		}
	}

	private static bool LooksLikeInternalId(string value)
	{
		var normalized = value.Trim().ToLowerInvariant();
		return normalized.Length == 0
			|| normalized.All(char.IsDigit)
			|| HexIdPattern.IsMatch(normalized)
			|| HashLikePattern.IsMatch(normalized);
	}
}