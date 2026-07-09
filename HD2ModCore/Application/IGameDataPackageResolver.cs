using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：抽象读取 HD2 原版游戏 archive TOC 与资源 payload 的能力，供目标模板读取流程使用。
// Purpose: Abstracts reading HD2 vanilla game archive TOCs and payloads for target template loading.
public interface IGameDataPackageResolver
{
	ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default);

	ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default);
}
