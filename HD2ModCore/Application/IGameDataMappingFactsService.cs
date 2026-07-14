using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Maps AssetKeys to all indexed Game Data targets and publishes explicit mapping generations.
public interface IGameDataMappingFactsService
{
	ValueTask<GameDataMappingFacts> MapAsync(
		IReadOnlySet<AssetKey> assetKeys,
		CancellationToken cancellationToken = default);
}
