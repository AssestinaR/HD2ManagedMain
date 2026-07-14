namespace HD2ModCore.Domain;

// Purpose: Game Data mapping for one AssetKey, retaining every matching target archive and readable type/name.
public sealed record GameDataMappedAssetFact(
	AssetKey AssetKey,
	string FileDisplayName,
	string TypeDisplayName,
	AssetTypeCategory TypeCategory,
	IReadOnlyList<ArchiveMetadata> TargetArchives);
