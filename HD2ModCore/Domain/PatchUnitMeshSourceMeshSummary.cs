namespace HD2ModCore.Domain;

// 作用：描述 source Unit 中一个可用于替换的 RawMesh 的结构摘要。
// Purpose: Describes structural summary for one RawMesh that can be used as a replacement source.
public sealed record PatchUnitMeshSourceMeshSummary(
	int MeshInfoIndex,
	uint MeshId,
	int LodIndex,
	uint StreamIndex,
	uint VertexCount,
	uint IndexCount,
	uint MaterialCount,
	uint SectionCount,
	uint VertexStride,
	IReadOnlyList<UnitMeshReplacementComponentSignature> ComponentLayout);
