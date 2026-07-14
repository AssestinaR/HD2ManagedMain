namespace HD2ModCore.Domain;

// Purpose: Core-owned archive browser projection combining indexed Game Data, library, expected and actual facts.
public sealed record GameDataEffectiveAsset(
	AssetKey AssetKey,
	ModNodeId? WinnerNodeId,
	int TargetPatchIndex,
	bool HasCompetition);

public sealed record GameDataArchiveOverlay(
	string PackageName,
	IReadOnlyList<ModNodeId> LibraryModIds,
	IReadOnlyList<ModNodeId> ActiveModIds,
	IReadOnlyList<ModNodeId> EffectiveModIds,
	IReadOnlyList<int> EffectiveTargetPatchIndexes,
	IReadOnlyList<GameDataEffectiveAsset> EffectiveAssets,
	bool HasCompetition,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool HasLibraryReplacement => LibraryModIds.Count > 0;
	public bool HasActiveReplacement => ActiveModIds.Count > 0;
	public bool HasEffectiveReplacement => EffectiveModIds.Count > 0;
}

public sealed record GameDataArchiveBrowserItem(
	GameDataArchiveSummary Archive,
	GameDataArchiveOverlay Overlay);

public sealed record GameDataArchiveBrowserSnapshot(
	GameDataIndexFingerprint Fingerprint,
	ProfileId? ActiveProfileId,
	IReadOnlyList<GameDataArchiveBrowserItem> Archives,
	IReadOnlyDictionary<ModNodeId, string> ModNames,
	IReadOnlyList<CoreIssue> Issues);
