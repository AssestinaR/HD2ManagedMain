namespace HD2ModCore.Domain;

// 作用：描述 source catalog 中一个可选的 Unit mesh entry 及其 RawMesh 摘要。
// Purpose: Describes one selectable Unit mesh entry in the source catalog and its RawMesh summaries.
public sealed record PatchUnitMeshSourceCatalogEntry(
	PatchTocEntry Entry,
	uint Version,
	ulong NameHash,
	IReadOnlyList<PatchUnitMeshSourceMeshSummary> Meshes)
{
	public int MeshCount => Meshes.Count;
}
