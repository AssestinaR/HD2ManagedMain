using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Produces readable Mod summaries solely from stable content facts and Game Data mapping projections.
public sealed class ModAssetSummaryProjector
{
	private readonly IGameDataMappingFactsService mappingFactsService;
	private readonly IAssetMetadataCatalogProvider? fallbackCatalogProvider;

	public ModAssetSummaryProjector(IGameDataMappingFactsService mappingFactsService, IAssetMetadataCatalogProvider? fallbackCatalogProvider = null)
	{
		this.mappingFactsService = mappingFactsService ?? throw new ArgumentNullException(nameof(mappingFactsService));
		this.fallbackCatalogProvider = fallbackCatalogProvider;
	}

	public async ValueTask<ModAssetSummary> ProjectAsync(ModNode node, ModContentFacts facts, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(facts);
		var sourceByKey = BuildSourceByKey(facts);
		var mapping = await mappingFactsService.MapAsync(sourceByKey.Keys.ToHashSet(), cancellationToken).ConfigureAwait(false);
		return Project(node, sourceByKey, mapping, await ResolveCatalogAsync(mapping, cancellationToken).ConfigureAwait(false));
	}

	public async ValueTask<IReadOnlyDictionary<ModNodeId, ModAssetSummary>> ProjectManyAsync(IReadOnlyDictionary<ModNode, ModContentFacts> factsByNode, CancellationToken cancellationToken = default)
		=> (await ProjectManyWithGenerationAsync(factsByNode, cancellationToken).ConfigureAwait(false)).Summaries;

	public async ValueTask<ModAssetSummaryProjection> ProjectManyWithGenerationAsync(IReadOnlyDictionary<ModNode, ModContentFacts> factsByNode, CancellationToken cancellationToken = default)
	{
		var sourceByNode = factsByNode.ToDictionary(pair => pair.Key, pair => BuildSourceByKey(pair.Value));
		var mapping = await mappingFactsService.MapAsync(sourceByNode.Values.SelectMany(value => value.Keys).ToHashSet(), cancellationToken).ConfigureAwait(false);
		var catalog = await ResolveCatalogAsync(mapping, cancellationToken).ConfigureAwait(false);
		return new ModAssetSummaryProjection(mapping.MappingGeneration, sourceByNode.ToDictionary(pair => pair.Key.Id, pair => Project(pair.Key, pair.Value, mapping, catalog)));
	}

	public async ValueTask<string> GetMappingGenerationAsync(CancellationToken cancellationToken = default)
		=> (await mappingFactsService.MapAsync(new HashSet<AssetKey>(), cancellationToken).ConfigureAwait(false)).MappingGeneration;

	private async ValueTask<AssetMetadataCatalog> ResolveCatalogAsync(GameDataMappingFacts mapping, CancellationToken cancellationToken)
		=> mapping.Catalog
			?? (fallbackCatalogProvider is null
				? AssetMetadataCatalog.Empty
				: await fallbackCatalogProvider.LoadAsync(cancellationToken).ConfigureAwait(false));

	private static Dictionary<AssetKey, SourceInfo> BuildSourceByKey(ModContentFacts facts)
		=> facts.PatchGroups
			.SelectMany(group => group.AssetKeys.Select(key => (key, group.Id.SourceArchiveHex, FileName: group.Files.FirstOrDefault(file => file.SidecarKind == PatchSidecarKind.Base)?.FileName ?? group.Id.ToString())))
			.GroupBy(item => item.key)
			.ToDictionary(group => group.Key, group => new SourceInfo(group.Select(item => item.SourceArchiveHex).First(), group.Select(item => item.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()));

	private static ModAssetSummary Project(ModNode node, IReadOnlyDictionary<AssetKey, SourceInfo> sourceByKey, GameDataMappingFacts mapping, AssetMetadataCatalog catalog)
	{
		var assets = sourceByKey
			.OrderBy(pair => pair.Key.TypeId)
			.ThenBy(pair => pair.Key.FileId)
			.Select(pair => CreateEntry(pair.Key, pair.Value, mapping.Assets.GetValueOrDefault(pair.Key), catalog))
			.ToArray();
		var tags = assets.SelectMany(asset => asset.DerivedTags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
		return new ModAssetSummary(node.Id, node.Metadata.Name, assets, tags, BuildTargetGroups(assets));
	}

	private static PatchAssetEntry CreateEntry(AssetKey key, SourceInfo source, GameDataMappedAssetFact? mapped, AssetMetadataCatalog catalog)
	{
		var archives = mapped?.TargetArchives ?? Array.Empty<ArchiveMetadata>();
		var archive = archives.FirstOrDefault();
		var fallbackArchive = catalog.FindArchive(source.SourceArchiveHex);
		var category = archive?.Category ?? fallbackArchive?.Category ?? "Unknown";
		var displayName = archive?.DisplayName ?? fallbackArchive?.DisplayName ?? "Mod 私有资源";
		var type = mapped?.TypeDisplayName ?? catalog.FindType(key.TypeId)?.Name ?? TypeName(key.TypeId);
		var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		if (category != "Unknown") tags.Add(category.ToLowerInvariant());
		if (key.TypeId == 0xe0a48d0be9a7453f || key.TypeId == 0xc4f0f4be7fb0c8d6) tags.Add("model");
		if (key.TypeId == 0xeac0b497876adedf) tags.Add("material");
		if (key.TypeId == 0xcd4238c6a0c69e32) tags.Add("texture");
		if (archive is not null) tags.Add(archive.DisplayName);
		return new PatchAssetEntry(
			new PatchAssetKey("stable-facts", key.TypeId, key.FileId), displayName, category, int.MaxValue, int.MaxValue,
			mapped?.FileDisplayName ?? catalog.FindFile(key.FileId)?.FriendlyName ?? $"0x{key.FileId:x16}", type, mapped?.TypeCategory ?? catalog.FindType(key.TypeId)?.Category ?? AssetTypeCategory.Unknown,
			tags.ToArray(), source.FileNames, archives.Select(item => item.ArchiveId).ToArray());
	}

	private static IReadOnlyList<ModAssetTargetGroup> BuildTargetGroups(IReadOnlyList<PatchAssetEntry> assets)
		=> assets
			.Where(asset => !string.Equals(asset.ArchiveCategory, "Unknown", StringComparison.OrdinalIgnoreCase))
			.GroupBy(asset => asset.ArchiveCategory, StringComparer.OrdinalIgnoreCase)
			.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Select((group, index) => new ModAssetTargetGroup(group.Key, index,
				group.GroupBy(asset => asset.ArchiveDisplayName, StringComparer.OrdinalIgnoreCase)
					.OrderBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
					.Select(target => new ModAssetTargetItem(target.Key, int.MaxValue, target.SelectMany(asset => asset.SemanticTargetArchiveIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), target.Select(asset => asset.TypeDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), target.Count()))
					.ToArray(), group.Count()))
			.ToArray();

	private static string TypeName(ulong typeId) => typeId switch
	{
		0xe0a48d0be9a7453f => "Unit",
		0xc4f0f4be7fb0c8d6 => "Composite Unit",
		0xeac0b497876adedf => "Material",
		0xcd4238c6a0c69e32 => "Texture",
		_ => $"0x{typeId:x16}",
	};

	private sealed record SourceInfo(string SourceArchiveHex, IReadOnlyList<string> FileNames);
}

// Purpose: Separates stable Mod content facts from the versioned Game Data label projection.
public sealed record ModAssetSummaryProjection(
	string MappingGeneration,
	IReadOnlyDictionary<ModNodeId, ModAssetSummary> Summaries);
