namespace HD2ModCore.Domain;

// Purpose: Ordered expected competition chain for one strict TypeID + FileID AssetKey.
public sealed record ProfileAssetOverrideChain(
	AssetKey AssetKey,
	IReadOnlyList<ProfileAssetOverrideEntry> Entries)
{
	public ProfileAssetOverrideEntry Winner => Entries[^1];
	public bool IsCompetition => Entries.Select(entry => entry.NodeId).Distinct().Skip(1).Any();
}
