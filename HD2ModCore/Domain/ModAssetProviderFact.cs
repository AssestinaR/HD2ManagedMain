namespace HD2ModCore.Domain;

// Purpose: Identifies a Mod/PatchGroup that contains a requested AssetKey.
public sealed record ModAssetProviderFact(
	ModNodeId NodeId,
	string PatchGroupId,
	AssetKey AssetKey);
