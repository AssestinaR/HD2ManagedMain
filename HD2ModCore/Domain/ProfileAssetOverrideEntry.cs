namespace HD2ModCore.Domain;

// Purpose: One mod's participation in an expected AssetKey override chain.
public sealed record ProfileAssetOverrideEntry(
	ModNodeId NodeId,
	string ModName,
	int LoadOrder,
	IReadOnlyList<ModPatchGroupId> PatchGroups,
	GameDataMappedAssetFact Mapping,
	bool IsWinner);
