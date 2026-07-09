namespace HD2ModCore.Domain;

// Purpose: Summarizes one semantic replacement target within a mod asset summary.
public sealed record ModAssetTargetItem(
	string DisplayName,
	int ArchiveOrder,
	IReadOnlyList<string> ArchiveIds,
	IReadOnlyList<string> TypeNames,
	int AssetCount);