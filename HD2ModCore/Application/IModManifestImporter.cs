using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从导出的 zip（包含 manifest.json）导入可移植名称和备注元数据。
// Purpose: Imports portable name and notes metadata from an exported zip containing manifest.json.
public interface IModManifestImporter
{
	ValueTask<ImportResult> ImportExportZipAsync(string zipFilePath, CancellationToken cancellationToken = default);
}
