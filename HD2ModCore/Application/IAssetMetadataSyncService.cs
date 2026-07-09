using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：同步并缓存社区资产元数据表。
// Purpose: Synchronizes and caches community asset metadata hash lists.
public interface IAssetMetadataSyncService
{
	ValueTask<AssetMetadataSyncResult> SyncAsync(string repositoryRawBaseUrl, CancellationToken cancellationToken = default);
}