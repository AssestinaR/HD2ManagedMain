namespace HD2ModCore.Domain;

// 作用：描述 patch 文件夹中可参与自动化 dry-run 的 TOC 文件集合。
// Purpose: Describes patch TOC files collected from a directory for automation dry-runs.
public sealed record PatchTocFileSet(
	string RootDirectoryPath,
	IReadOnlyList<string> PatchTocFilePaths)
{
	public int Count => PatchTocFilePaths.Count;
}
