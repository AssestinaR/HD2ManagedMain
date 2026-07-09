namespace HD2ModCore.Domain;

// Purpose: Groups replacement targets for UI display using archivehashes.json category ordering.
public sealed record ModAssetTargetGroup(
	string Category,
	int CategoryOrder,
	IReadOnlyList<ModAssetTargetItem> Items,
	int AssetCount);