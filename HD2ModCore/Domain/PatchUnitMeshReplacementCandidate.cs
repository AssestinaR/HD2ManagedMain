namespace HD2ModCore.Domain;

// 作用：描述一个 patch entry 上可执行的 Unit RawMesh 自动替换候选。
// Purpose: Describes one executable Unit RawMesh automatic replacement candidate for a patch entry.
public sealed record PatchUnitMeshReplacementCandidate(
	PatchTocEntry TargetEntry,
	PatchTocEntry SourceEntry,
	UnitMeshReplacementCandidate MeshCandidate);
