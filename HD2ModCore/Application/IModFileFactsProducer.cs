using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从 Mod 文件系统生成基础部署所需的 FileFacts，不依赖派生分析缓存。
// Purpose: Produces deployment FileFacts directly from the mod filesystem without derived analysis.
public interface IModFileFactsProducer
{
	ValueTask<PatchFileIndex> ProduceAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}