using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从模组库真实文件系统扫描生成临时 patch 索引缓存。
// Purpose: Builds a temporary patch index cache by scanning real files in the mod library.
public interface IPatchFileIndexBuilder
{
	ValueTask<PatchFileIndex> BuildAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}