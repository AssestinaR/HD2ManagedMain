using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义从 patch 文件夹构建可选 source Unit mesh catalog 的 API，供自动化 dry-run 前选择源 mesh。
// Purpose: Defines APIs for building selectable source Unit mesh catalogs from patch directories before automation dry-runs.
public interface IPatchUnitMeshSourceCatalogBuilder
{
	ValueTask<PatchUnitMeshSourceCatalog> BuildCatalogAsync(
		string patchDirectoryPath,
		CancellationToken cancellationToken = default);
}
