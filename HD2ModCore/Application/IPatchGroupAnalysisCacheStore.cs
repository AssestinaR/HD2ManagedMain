using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Persists Adaptation patch facts independently from Core projections.
public interface IPatchGroupAnalysisCacheStore
{
	ValueTask<PatchGroupAnalysisCacheEntry?> TryLoadAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
	ValueTask SaveAsync(PatchGroupAnalysisCacheEntry entry, CancellationToken cancellationToken = default);
}

public sealed record PatchGroupAnalysisCacheEntry(
	int Version,
	ModNodeId NodeId,
	string RelativePath,
	IReadOnlyList<PatchAssetSourceFileFingerprint> SourceFiles,
	DateTimeOffset BuiltAtUtc,
	IReadOnlyList<PatchGroupAnalysis> Analyses);
