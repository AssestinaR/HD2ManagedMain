using HD2ModCore.Domain;

namespace HD2ModCore.Application;

public interface IAssetArchiveIndexService
{
	ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default);

	ValueTask<GameDataIndexFingerprint?> GetFingerprintAsync(CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<GameDataArchiveSummary>> GetArchiveSummariesAsync(
		CancellationToken cancellationToken = default);

	ValueTask<GameDataArchiveDetails?> GetArchiveDetailsAsync(
		string packageName,
		CancellationToken cancellationToken = default);

	ValueTask<GameDataIndexStatus> GetIndexStatusAsync(
		string gameDataDirectory,
		string archiveHashesJson,
		CancellationToken cancellationToken = default);

	ValueTask BuildOrRebuildAsync(
		string gameDataDirectory,
		string archiveHashesJson,
		IProgress<IndexBuildProgress>? progress = null,
		CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<AssetArchiveMatch>> FindAssetArchivesAsync(
		IReadOnlySet<AssetKey> assetKeys,
		CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(
		IReadOnlySet<AssetKey> assetKeys,
		IndexFilterSettings filterSettings,
		CancellationToken cancellationToken = default);
}
