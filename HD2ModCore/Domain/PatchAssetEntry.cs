namespace HD2ModCore.Domain;

// Purpose: Readable projection of a patch TOC entry enriched with asset metadata.
public sealed record PatchAssetEntry(
	PatchAssetKey Key,
	string ArchiveDisplayName,
	string ArchiveCategory,
	int ArchiveCategoryOrder,
	int ArchiveOrder,
	string FileDisplayName,
	string TypeDisplayName,
	AssetTypeCategory TypeCategory,
	IReadOnlyList<string> DerivedTags,
	IReadOnlyList<string> SourceFiles,
	IReadOnlyList<string>? TargetArchiveIds = null)
{
	public string DisplayName => $"{ArchiveDisplayName} / {FileDisplayName} ({TypeDisplayName})";
	public IReadOnlyList<string> SemanticTargetArchiveIds => TargetArchiveIds ?? Array.Empty<string>();
}