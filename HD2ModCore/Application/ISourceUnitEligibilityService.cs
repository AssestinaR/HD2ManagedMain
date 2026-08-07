using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Provides one source-patch eligibility rule shared by all model-transfer workflows.
public interface ISourceUnitEligibilityService
{
	SourceUnitEligibilitySelection Select(IReadOnlyList<PatchGroupAnalysis> analyses);
}

public sealed record SourceUnitEligibility(
	AssetKey UnitAssetKey,
	bool IsEligible,
	string Reason);

public sealed record SourceUnitEligibilitySelection(
	IReadOnlySet<AssetKey> EligibleUnitAssetKeys,
	IReadOnlyList<SourceUnitEligibility> Units);
