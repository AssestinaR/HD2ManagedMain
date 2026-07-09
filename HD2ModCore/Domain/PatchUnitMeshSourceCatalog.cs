namespace HD2ModCore.Domain;

// 作用：描述从 patch 文件夹解析出的可作为 RawMesh 替换源的 Unit mesh catalog。
// Purpose: Describes Unit mesh entries parsed from a patch directory that can act as RawMesh replacement sources.
public sealed record PatchUnitMeshSourceCatalog(
	string PatchDirectoryPath,
	IReadOnlyList<string> PatchTocFilePaths,
	IReadOnlyList<PatchTocEntry> ScannedEntries,
	IReadOnlyList<PatchUnitMeshSourceCatalogEntry> Entries,
	IReadOnlyList<PatchUnitMeshSourceCatalogFailure> Failures)
{
	public int PatchCount => PatchTocFilePaths.Count;

	public int EntryCount => Entries.Count;

	public int FailureCount => Failures.Count;
}
