namespace HD2ModCore.Domain;

// Purpose: Immutable actual Data deployment graph independent from the expected profile graph.
public sealed record DeployedOverrideGraph(
	string GameDataDirectory,
	string DeploymentGeneration,
	DateTimeOffset BuiltUtc,
	ProfileId? RecordedProfileId,
	long RecordedProfileRevision,
	IReadOnlyList<DeployedPatchGroupFact> PatchGroups,
	IReadOnlyList<DeployedAssetOverrideChain> AssetChains,
	IReadOnlyList<CoreIssue> Issues);
