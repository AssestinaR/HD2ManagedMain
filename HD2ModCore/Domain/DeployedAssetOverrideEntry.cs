namespace HD2ModCore.Domain;

// Purpose: One deployed patch group's participation in an actual AssetKey winner chain.
public sealed record DeployedAssetOverrideEntry(
	string ArchiveHex16,
	int TargetPatchIndex,
	ModPatchGroupId? SourcePatchGroupId,
	ModNodeId? NodeId,
	bool IsWinner);
