using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Produces shallow AssetInventory facts without reference-graph or SQLite prerequisites.
public interface IAssetInventoryProducer
{
	ValueTask<ModContentFacts> GetNodeFactsAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}

// Purpose: Supplies the exact generation used by the AssetInventory producer for request coalescing and cache lookup.
public interface IAssetInventoryGenerationProvider
{
	string ComputeGeneration(ModNode node, string modsRootDirectory);
}