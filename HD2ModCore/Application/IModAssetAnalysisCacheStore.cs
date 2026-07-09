using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Persists and retrieves per-node asset analysis summaries with source file fingerprints.
public interface IModAssetAnalysisCacheStore
{
	ValueTask<ModAssetAnalysisCacheEntry?> TryLoadAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
	ValueTask SaveAsync(ModAssetAnalysisCacheEntry entry, CancellationToken cancellationToken = default);
}