namespace HD2ModCore.Domain;

// Purpose: Asset-level override analysis for an ordered mod list.
public sealed record ModAssetOverrideAnalysis(
	IReadOnlyList<ModAssetSummary> Summaries,
	IReadOnlyList<ModAssetOverrideChain> OverrideChains,
	IReadOnlyList<ModOverrideCoverage> Coverages);