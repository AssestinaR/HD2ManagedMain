namespace HD2ModCore.Domain;

// Purpose: Immutable expected override graph for one profile revision and its content/mapping generations.
public sealed record ProfileOverrideGraph(
	ProfileId ProfileId,
	long ProfileRevision,
	string GraphGeneration,
	string MappingGeneration,
	DateTimeOffset BuiltUtc,
	IReadOnlyDictionary<ModNodeId, string> ContentGenerations,
	IReadOnlyList<ProfileAssetOverrideChain> AssetChains,
	IReadOnlyList<ProfileArchiveOverlap> ArchiveOverlaps,
	IReadOnlyList<ProfileModCoverage> Coverages,
	IReadOnlyList<CoreIssue> Issues);
