namespace HD2ModCore.Domain;

// 作用：描述一个 source RawMesh 可以安全替换 target RawMesh slot 的结构匹配候选。
// Purpose: Describes one structural match where a source RawMesh can safely replace a target RawMesh slot.
public sealed record UnitMeshReplacementCandidate(
	int TargetMeshInfoIndex,
	int SourceMeshInfoIndex,
	uint TargetMeshId,
	uint SourceMeshId,
	string TargetSemanticName,
	string SourceSemanticName,
	int LodIndex,
	uint StreamIndex,
	uint VertexStride,
	IReadOnlyList<UnitMeshReplacementComponentSignature> ComponentLayout,
	UnitMeshReplacementCandidateKind Kind,
	int Score,
	string Reason);
