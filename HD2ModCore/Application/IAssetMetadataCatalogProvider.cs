using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Loads the cached community asset metadata catalog for archive, file and type lookups.
public interface IAssetMetadataCatalogProvider
{
	ValueTask<AssetMetadataCatalog> LoadAsync(CancellationToken cancellationToken = default);
}