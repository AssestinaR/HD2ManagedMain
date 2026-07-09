using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义从原版游戏 archive 读取目标 Unit mesh 模板的 API，作为人工流程自动化的目标模板入口。
// Purpose: Defines APIs for reading vanilla game archive Unit mesh templates used as adaptation targets.
public interface IArchiveUnitMeshReader
{
	ValueTask<ArchiveUnitMesh> ReadUnitMeshAsync(
		string gameDataDirectory,
		string archiveName,
		AssetKey assetKey,
		CancellationToken cancellationToken = default);
}
