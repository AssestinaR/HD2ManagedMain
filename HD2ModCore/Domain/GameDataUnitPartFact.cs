using HD2ModAdaptation.Analysis;

namespace HD2ModCore.Domain;

// Purpose: Stores the stable semantic part projection for one vanilla Unit mesh.
public sealed record GameDataUnitPartFact(
	string ArchiveId,
	AssetKey UnitAssetKey,
	int MeshInfoIndex,
	uint MeshId,
	UnitMeshPartKind PartKind,
	UnitMeshPartLayer Layer,
	UnitMeshBodyVariant BodyVariant,
	string SemanticName,
	int Confidence,
	bool IsVisualMesh,
	bool IsLod,
	string Reason);