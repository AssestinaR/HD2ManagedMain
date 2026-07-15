namespace HD2ModCore.Domain;

// Purpose: Provides detailed persisted facts for one indexed Game Data archive.
public sealed record GameDataArchiveDetails(
	GameDataArchiveSummary Summary,
	IReadOnlyList<GameDataArchiveAssetEntry> Assets,
	IReadOnlyList<CoreIssue> Issues);

public sealed record GameDataArchiveAssetEntry(
	AssetKey AssetKey,
	string TypeName,
	string FriendlyName,
	string PartSummary,
	IReadOnlyList<string> SharedPackages,
	IReadOnlyList<string> SharedDisplayNames);
