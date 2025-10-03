using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从导出的 zip（包含 manifest.json）导入，兼容本管理器的自定义标签字段。
// Purpose: Imports from an exported zip (with manifest.json), including this manager's custom user tags field.
public interface IModManifestImporter
{
	ValueTask<ImportResult> ImportExportZipAsync(string zipFilePath, CancellationToken cancellationToken = default);
}
