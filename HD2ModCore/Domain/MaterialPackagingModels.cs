namespace HD2ModCore.Domain;

// Purpose: Defines Core-facing material packaging state, candidate and verified output results.
public sealed record ModMaterialPackagingState(
	ModNodeId NodeId,
	string? PatchTocPath,
	bool CanSplit,
	bool HasEmbeddedMaterials,
	bool HasExternalMaterials,
	int RequiredMaterialCount,
	int EmbeddedMaterialCount,
	int ExternalMaterialCount,
	int EmbeddedTextureCount,
	IReadOnlyList<string> Blockers);

public sealed record MaterialPackageCandidate(
	ModNodeId NodeId,
	string Name,
	bool IsCompatible,
	int MatchingMaterialCount,
	int MissingMaterialCount,
	int MissingTextureCount,
	IReadOnlyList<string> Blockers);

public sealed record MaterialPackagingOperationResult(
	bool IsSuccessful,
	IReadOnlyList<string> OutputDirectories,
	int AssetCount,
	int GraphEdgeCount,
	IReadOnlyList<CoreIssue> Issues);