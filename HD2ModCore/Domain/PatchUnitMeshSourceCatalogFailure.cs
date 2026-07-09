namespace HD2ModCore.Domain;

// 作用：描述构建 source Unit mesh catalog 时某个 patch entry 的失败原因。
// Purpose: Describes why one patch entry failed while building a source Unit mesh catalog.
public sealed record PatchUnitMeshSourceCatalogFailure(
	PatchTocEntry Entry,
	string Reason,
	Exception? Exception = null,
	bool IsUnsupportedUnitMeshFormat = false);
