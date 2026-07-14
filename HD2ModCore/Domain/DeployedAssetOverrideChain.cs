namespace HD2ModCore.Domain;

// Purpose: Actual winner chain for one strict TypeID + FileID AssetKey, ordered by deployed target index.
public sealed record DeployedAssetOverrideChain(
	AssetKey AssetKey,
	IReadOnlyList<DeployedAssetOverrideEntry> Entries)
{
	public DeployedAssetOverrideEntry Winner => Entries[^1];
	public bool IsCompetition => Entries.Count > 1;
}
