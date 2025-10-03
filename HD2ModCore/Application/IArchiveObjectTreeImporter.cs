using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从压缩包导入并生成对象树（支持 zip/7z/rar 等）。
// Purpose: Imports from an archive and generates an object tree (supports zip/7z/rar, etc.).
public interface IArchiveObjectTreeImporter
{
	ValueTask<ImportedObjectTree> ImportArchiveAsync(
		string archiveFilePath,
		CancellationToken cancellationToken = default);
}
