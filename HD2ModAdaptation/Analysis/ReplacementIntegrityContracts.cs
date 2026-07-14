using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Defines neutral Mod replacement coverage facts and risk findings.
public enum ReplacementRiskLevel
{
	Informational,
	Low,
	Medium,
	High
}

public sealed record ReplacementCoverageFinding(
	string ItemName,
	AssetKey SharedAsset,
	ResourceReuseLevel ReuseLevel,
	ReplacementRiskLevel RiskLevel,
	IReadOnlyList<string> CoveredItems,
	IReadOnlyList<string> UncoveredItems,
	string Explanation,
	string SourceFingerprint);

public interface IReplacementIntegrityAnalyzer
{
	IReadOnlyList<ReplacementCoverageFinding> Analyze(
		IReadOnlyList<GameItemResourceInfo> items,
		IReadOnlyList<ResourceReuseGroup> reuseGroups,
		IReadOnlyCollection<AssetKey> modifiedAssets,
		string sourceFingerprint);
}
