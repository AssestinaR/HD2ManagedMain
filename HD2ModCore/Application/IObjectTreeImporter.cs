using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从文件夹或压缩包导入并生成对象树（目录节点即对象）。
// Purpose: Imports from a folder or archive and generates an object tree (each directory node is an object).
public interface IObjectTreeImporter
{
	ValueTask<ImportedObjectTree> ImportFolderAsync(
		string folderPath,
		CancellationToken cancellationToken = default);
}
