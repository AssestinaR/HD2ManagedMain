namespace HD2ModCore.Domain;

// 作用：标识 Unit RawMesh 替换候选的结构匹配强度。
// Purpose: Identifies the structural match strength of a Unit RawMesh replacement candidate.
public enum UnitMeshReplacementCandidateKind
{
	LayoutOnly = 0,
	SameLod = 1,
	SameLodAndMaterialSlots = 2,
	SameMeshId = 3,
	ExperimentalFallback = 4,
	SdkStreamTranscode = 5,
}
