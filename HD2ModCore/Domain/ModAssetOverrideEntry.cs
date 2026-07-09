namespace HD2ModCore.Domain;

// Purpose: One mod's participation in an asset override chain.
public sealed record ModAssetOverrideEntry(
	ModNodeId NodeId,
	string ModName,
	int LoadOrder,
	PatchAssetEntry Asset,
	bool IsWinner);