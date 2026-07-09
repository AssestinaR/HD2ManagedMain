namespace HD2ModCore.Domain;

// Purpose: Reports how well a mod's patched asset keys match the current game data reverse index.
public sealed record ModCompatibilityReport(
	ModNodeId NodeId,
	string Name,
	ModCompatibilityStatus Status,
	int TotalAssets,
	int MatchedAssets,
	int MissingAssets,
	double MatchRatio,
	IReadOnlyList<AssetArchiveMatch> Matches,
	GameDataIndexFingerprint? IndexFingerprint)
{
	public bool HasIndex => IndexFingerprint is not null;
}