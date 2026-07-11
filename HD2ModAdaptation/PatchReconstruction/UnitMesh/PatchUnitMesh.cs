namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// 浣滅敤锛氫繚瀛樹粠 patch entry 瑙ｆ瀽鍑虹殑 Unit mesh 浠ュ強瀵瑰簲鍘熷 payload銆?
// Purpose: Holds a Unit mesh parsed from one patch entry together with its original payloads.
public sealed record PatchUnitMesh(
	PatchTocEntry Entry,
	PatchEntryPayload Payload,
	UnitMeshModel Model,
	PatchEntryPayload? CompositePayload = null,
	PatchUnitDependencyResolution? Dependencies = null);

// Purpose: Records whether Unit auxiliary references were resolved from the source patch or intentionally left external.
public sealed record PatchUnitDependencyResolution(
	ulong BonesReference,
	ulong CompositeReference,
	bool IsBoneResolvedFromPatch,
	bool IsCompositeResolvedFromPatch)
{
	public bool HasUnresolvedExternalBone => BonesReference != 0 && !IsBoneResolvedFromPatch;
	public bool HasUnresolvedExternalComposite => CompositeReference != 0 && !IsCompositeResolvedFromPatch;
}

// Purpose: Holds a Unit mesh read from an explicitly selected vanilla game archive.
public sealed record GameDataUnitMesh(
	AssetKey AssetKey,
	string ArchiveName,
	PatchEntryPayload Payload,
	UnitMeshModel Model,
	PatchEntryPayload? CompositePayload = null);
