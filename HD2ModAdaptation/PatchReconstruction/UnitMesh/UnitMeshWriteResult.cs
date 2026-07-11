namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// 浣滅敤锛氳〃绀?Unit mesh 鍐欏洖鍚庣殑 TocData 涓?GPU sidecar 浜岃繘鍒剁粨鏋溿€?
// Purpose: Represents serialized Unit TocData and GPU sidecar data produced by the Unit mesh writer.
public sealed record UnitMeshWriteResult(
	byte[] TocData,
	byte[] GpuData,
	byte[]? CompositeTocData = null,
	byte[]? CompositeGpuData = null);
