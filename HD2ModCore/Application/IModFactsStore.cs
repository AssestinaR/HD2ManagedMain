using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Persists immutable imported Mod assets and reference edges in a dedicated SQLite database.
public interface IModFactsStore : IPatchGroupAnalysisCacheStore
{
	ValueTask DeleteAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<PatchAssetReference>> FindConsumersAsync(HD2ModAdaptation.PatchReconstruction.AssetKey targetAssetKey, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<ModAssetConsumerFact>> FindConsumerFactsAsync(HD2ModAdaptation.PatchReconstruction.AssetKey targetAssetKey, CancellationToken cancellationToken = default);
}