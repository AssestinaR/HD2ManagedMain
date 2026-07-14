using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Defines neutral resource-reuse facts and confidence levels for Item comparisons.
public enum ResourceReuseLevel
{
	ExactUnitReuse,
	CompositeReuse,
	MeshReuse,
	MaterialReuse,
	TextureOnlyReuse
}

public enum ResourceReuseConfidence
{
	High,
	Medium,
	Low
}

public sealed record ResourceReuseGroup(
	string GroupId,
	IReadOnlyList<string> ItemNames,
	AssetKey SharedAsset,
	ResourceReuseLevel Level,
	ResourceReuseConfidence Confidence,
	string Explanation,
	string SourceFingerprint);

public interface IResourceReuseDetector
{
	IReadOnlyList<ResourceReuseGroup> Detect(
		IReadOnlyList<GameItemResourceInfo> items,
		string sourceFingerprint);
}
