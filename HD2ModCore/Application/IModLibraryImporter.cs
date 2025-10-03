using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：将文件夹/压缩包导入并落库到程序目录的 mods/ 下，同时更新并保存 LibrarySnapshot。
// Purpose: Imports a folder/archive into the library under app-local mods/, updates and persists the LibrarySnapshot.
public interface IModLibraryImporter
{
	ValueTask<ImportResult> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default);
	ValueTask<ImportResult> ImportArchiveAsync(string archiveFilePath, CancellationToken cancellationToken = default);
}
