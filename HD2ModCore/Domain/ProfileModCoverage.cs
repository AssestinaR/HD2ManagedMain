namespace HD2ModCore.Domain;

// Purpose: Expected per-mod AssetKey coverage before deployment.
public sealed record ProfileModCoverage(
	ModNodeId NodeId,
	string ModName,
	int TotalAssetKeys,
	int WinningAssetKeys,
	int OverriddenAssetKeys)
{
	public bool FullyOverridden => TotalAssetKeys > 0 && OverriddenAssetKeys >= TotalAssetKeys;
	public bool PartiallyOverridden => OverriddenAssetKeys > 0 && !FullyOverridden;
}
