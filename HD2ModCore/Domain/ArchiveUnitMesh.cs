namespace HD2ModCore.Domain;

// 作用：保存从原版游戏 archive 读取出的目标 Unit mesh 模板及其原始 payload。
// Purpose: Holds a target Unit mesh template read from a vanilla game archive together with original payloads.
public sealed record ArchiveUnitMesh(
	ArchiveTocEntry Entry,
	ArchiveEntryPayload Payload,
	UnitMeshModel Model);
