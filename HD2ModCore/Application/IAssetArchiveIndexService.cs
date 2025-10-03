using HD2ModCore.Domain;

namespace HD2ModCore.Application;

public interface IAssetArchiveIndexService
{
	ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default);

	ValueTask BuildOrRebuildAsync(
		string gameDataDirectory,
		string archiveHashesJson,
		IProgress<IndexBuildProgress>? progress = null,
		CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(
		IReadOnlySet<AssetKey> assetKeys,
		IndexFilterSettings filterSettings,
		CancellationToken cancellationToken = default);
}
