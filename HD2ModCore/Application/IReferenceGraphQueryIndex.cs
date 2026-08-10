using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：提供 ReferenceGraph 的反向消费者查询，不承担普通派生缓存职责。
// Purpose: Provides reverse consumer queries for ReferenceGraph without owning ordinary derived caches.
public interface IReferenceGraphQueryIndex
{
	ValueTask<IReadOnlyList<ModAssetConsumerFact>> FindConsumerFactsAsync(HD2ModAdaptation.PatchReconstruction.AssetKey targetAssetKey, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<ModAssetProviderFact>> FindProviderFactsAsync(
		IReadOnlyCollection<HD2ModAdaptation.PatchReconstruction.AssetKey> assetKeys,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<ModAssetProviderFact>>(Array.Empty<ModAssetProviderFact>());

	async ValueTask<IReadOnlyList<ModAssetConsumerFact>> FindConsumerFactsAsync(
		IReadOnlyCollection<HD2ModAdaptation.PatchReconstruction.AssetKey> targetAssetKeys,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(targetAssetKeys);
		var results = new List<ModAssetConsumerFact>();
		foreach (var targetAssetKey in targetAssetKeys.Distinct())
		{
			cancellationToken.ThrowIfCancellationRequested();
			results.AddRange(await FindConsumerFactsAsync(targetAssetKey, cancellationToken).ConfigureAwait(false));
		}
		return results;
	}
}

// 作用：原子替换和删除由信息中心生产的引用图索引，避免查询消费者依赖写入能力。
// Purpose: Atomically replaces and removes information-center reference graph index rows.
public interface IReferenceGraphIndexWriter
{
	ValueTask ReplaceNodeAsync(ReferenceGraphFacts facts, CancellationToken cancellationToken = default);
	ValueTask ReplaceNodeAsync(AdvancedUnitAnalysisFacts facts, CancellationToken cancellationToken = default);
	ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
}
