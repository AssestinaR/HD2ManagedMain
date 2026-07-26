using HD2ModAdaptation.Analysis;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;

namespace HD2ModCore.Domain;

// Purpose: Describes a Mod's persisted material delivery shape and safe library material-provider candidates.
public enum MaterialDeliveryMode
{
	Unknown,
	NoMaterialDependencies,
	MaterialOnly,
	EmbeddedComplete,
	EmbeddedIncomplete,
	ExternalResolved,
	ExternalUnresolved,
	Mixed
}

public sealed record MaterialDeliveryCandidate(
	ModNodeId NodeId,
	string Name,
	int CoveredMaterialCount,
	int MissingTextureCount,
	bool IsComplete,
	IReadOnlyCollection<AdaptationAssetKey>? ClosureAssetKeys = null);

public sealed record MaterialReferenceFact(
	AdaptationAssetKey SourceAssetKey,
	AdaptationAssetKey TargetMaterialKey,
	PatchReferenceKind Kind);

public sealed record MaterialDeliveryFacts(
	ModNodeId NodeId,
	MaterialDeliveryMode Mode,
	int UnitCount,
	int RequiredMaterialCount,
	int EmbeddedMaterialCount,
	int ExternalMaterialCount,
	int MissingEmbeddedTextureCount,
	IReadOnlyList<MaterialDeliveryCandidate> Candidates,
	IReadOnlyList<string> Notices,
	IReadOnlyCollection<AdaptationAssetKey>? EmbeddedClosureAssetKeys = null,
	bool IsMaterialOnly = false,
	IReadOnlyCollection<AdaptationAssetKey>? GameDataMappedMaterialKeys = null,
	IReadOnlyList<MaterialReferenceFact>? SelfMaterialReferences = null)
{
	public bool CanRebuildModelOnly => Mode == MaterialDeliveryMode.ExternalResolved;
	public bool CanRebuildAsWhole => Mode == MaterialDeliveryMode.EmbeddedComplete;
}