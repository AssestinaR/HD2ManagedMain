namespace HD2ModCore.Domain;

// 作用：保存从 patch entry 解析出的 Unit mesh 以及对应原始 payload。
// Purpose: Holds a Unit mesh parsed from one patch entry together with its original payloads.
public sealed record PatchUnitMesh(
	PatchTocEntry Entry,
	PatchEntryPayload Payload,
	UnitMeshModel Model);
