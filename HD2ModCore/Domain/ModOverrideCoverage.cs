namespace HD2ModCore.Domain;

// Purpose: Per-mod coverage status after analyzing ordered asset overrides.
public sealed record ModOverrideCoverage(
	ModNodeId NodeId,
	string ModName,
	int TotalAssets,
	int OverriddenAssets,
	bool FullyOverridden)
{
	public bool PartiallyOverridden => OverriddenAssets > 0 && !FullyOverridden;
}