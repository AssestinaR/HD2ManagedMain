namespace HD2ModCore.Domain;

// Purpose: Projects one Mod AssetKey into the advanced table with stable facts and transient Profile state.
public sealed record AdvancedModAssetRow(
	AssetKey AssetKey,
	string TypeName,
	string ResourceName,
	string PartSummary,
	string TargetSummary,
	string ReferenceSummary,
	string ProviderSummary,
	string ProfileStatus,
	string DiagnosticSummary,
	string PatchGroupSummary,
	long TocBytes,
	long StreamBytes,
	long GpuBytes);