using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：持久化基础 FileFacts，缓存不可用时不阻断文件系统直接生产。
// Purpose: Persists FileFacts without making the cache a prerequisite for filesystem production.
public interface IModFileFactsCache
{
	ValueTask<PatchFileIndex?> TryLoadAsync(string generation, CancellationToken cancellationToken = default);
	ValueTask SaveAsync(string generation, PatchFileIndex facts, CancellationToken cancellationToken = default);
	ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
}