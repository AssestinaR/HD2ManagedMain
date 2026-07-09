namespace HD2ModCore.Domain;

// Purpose: Snapshot fingerprint describing the game data files used to build the asset reverse index.
public sealed record GameDataIndexFingerprint(
	string GameDataDirectory,
	DateTimeOffset BuiltUtc,
	int ArchivesTotal,
	int ArchivesIndexed,
	int AssetKeysTotal,
	string SourceFingerprint);