using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Produces shallow AssetInventory facts without reference-graph or SQLite prerequisites.
public interface IAssetInventoryProducer : IModContentFactsService
{
}