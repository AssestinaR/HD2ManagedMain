using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：统一持久化非 FileFacts 信息产品的缓存信封与节点清理能力。
// Purpose: Persists non-FileFacts information products with a common envelope and node cleanup.
public interface IModInformationCache
{
	ValueTask<T?> TryLoadAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, CancellationToken cancellationToken = default);
	ValueTask<ModInformationCacheEntry<T>?> TryLoadLatestAsync<T>(ModInformationKind kind, ModNodeId nodeId, CancellationToken cancellationToken = default);
	ValueTask SaveAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, T data, CancellationToken cancellationToken = default);
	ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
}

public sealed record ModInformationCacheEntry<T>(string Generation, T Data, DateTimeOffset BuiltUtc);
