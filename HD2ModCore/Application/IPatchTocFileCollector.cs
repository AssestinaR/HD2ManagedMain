using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义从文件夹中收集严格 .patch_数字 TOC 文件的 API，排除 sidecar 文件。
// Purpose: Defines APIs for collecting strict .patch_number TOC files from a directory while excluding sidecars.
public interface IPatchTocFileCollector
{
	PatchTocFileSet Collect(string patchDirectoryPath);
}
