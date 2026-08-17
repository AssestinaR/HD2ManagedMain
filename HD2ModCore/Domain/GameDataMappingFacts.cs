namespace HD2ModCore.Domain;

// Purpose: Immutable Game Data mapping snapshot tied to index and readable metadata generations.
public sealed record GameDataMappingFacts(
	string MappingGeneration,
	string IndexGeneration,
	string MetadataGeneration,
	DateTimeOffset BuiltUtc,
	IReadOnlyDictionary<AssetKey, GameDataMappedAssetFact> Assets,
	IReadOnlyList<CoreIssue> Issues,
	AssetMetadataCatalog? Catalog = null);
