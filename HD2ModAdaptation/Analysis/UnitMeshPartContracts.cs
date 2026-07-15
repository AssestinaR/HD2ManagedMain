using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Defines read-only semantic facts that describe the visible armor role of each Unit mesh.
public enum UnitMeshPartKind
{
	Unknown,
	Head,
	Torso,
	Pelvis,
	LeftArm,
	RightArm,
	LeftLeg,
	RightLeg,
	LeftShoulder,
	RightShoulder,
	Accessory
}

public enum UnitMeshPartLayer
{
	Unknown,
	Undergarment,
	Armor,
	Accessory,
	Culling,
	Static
}

public enum UnitMeshBodyVariant
{
	Unknown,
	Slim,
	Stocky,
	Any,
	Other
}

public enum UnitMeshPartEvidenceKind
{
	Unknown,
	CustomizationInfo,
	BoneName,
	NameHeuristic
}

public sealed record UnitMeshPartFact(
	AssetKey UnitAssetKey,
	int MeshInfoIndex,
	uint MeshId,
	UnitMeshPartKind PartKind,
	UnitMeshPartLayer Layer,
	UnitMeshBodyVariant BodyVariant,
	string SemanticName,
	UnitMeshPartEvidenceKind EvidenceKind,
	int Confidence,
	bool IsVisualMesh,
	bool IsLod,
	string Reason);