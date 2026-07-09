namespace HD2ModCore.Domain;

// Purpose: Asset-level summary for one mod node, including derived tags from scanned patch contents.
public sealed record ModAssetSummary(
	ModNodeId NodeId,
	string Name,
	IReadOnlyList<PatchAssetEntry> Assets,
	IReadOnlyList<string> DerivedTags,
	IReadOnlyList<ModAssetTargetGroup> TargetGroups)
{
	public bool HasAssets => Assets.Count > 0;
}