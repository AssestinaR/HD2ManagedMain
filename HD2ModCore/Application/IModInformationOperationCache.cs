using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：提供一次信息读取流程内的显式内存复用，不把大型 Payload 自动写入持久缓存。
// Purpose: Provides explicit operation-scoped reuse without persisting large payloads implicitly.
public interface IModInformationOperationCache : IAsyncDisposable
{
	Guid OperationId { get; }
	long CapacityBytes { get; }
	long UsedBytes { get; }

	ValueTask<ModInformationMemoryCacheEntry<T>?> TryGetAsync<T>(
		ModInformationCacheKey key,
		CancellationToken cancellationToken = default);

	ValueTask SetAsync<T>(
		ModInformationCacheKey key,
		T value,
		long? estimatedBytes = null,
		CancellationToken cancellationToken = default);

	bool Remove(ModInformationCacheKey key);
	void Clear();
}

// 作用：返回流程缓存值及其预算/访问信息，便于诊断和淘汰策略使用。
// Purpose: Carries a cached operation value and its accounting metadata.
public sealed record ModInformationMemoryCacheEntry<T>(
	T Data,
	DateTimeOffset CreatedUtc,
	DateTimeOffset LastAccessUtc,
	long EstimatedBytes);
