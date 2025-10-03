using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：将对象节点映射为实际需要链接到游戏 data 目录的 patch 文件路径集合。
// Purpose: Resolves a mod node into actual patch file paths that should be linked into the game data directory.
public interface IModFileResolver
{
	ValueTask<IReadOnlyList<string>> ResolvePatchFilesAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}
