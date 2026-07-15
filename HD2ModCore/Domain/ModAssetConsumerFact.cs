using HD2ModAdaptation.Analysis;

namespace HD2ModCore.Domain;

// Purpose: Identifies one persisted Mod-side reference that consumes an AssetKey.
public sealed record ModAssetConsumerFact(
	ModNodeId NodeId,
	string PatchGroupId,
	PatchAssetReference Reference);