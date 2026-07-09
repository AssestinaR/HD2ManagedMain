namespace HD2ModCore.Domain;

// Purpose: Persisted asset analysis for one mod node, invalidated by source file and metadata fingerprints.
public sealed record ModAssetAnalysisCacheEntry(
	int Version,
	ModNodeId NodeId,
	string RelativePath,
	string MetadataFingerprint,
	DateTimeOffset BuiltAtUtc,
	IReadOnlyList<PatchAssetSourceFileFingerprint> SourceFiles,
	ModAssetSummary Summary);