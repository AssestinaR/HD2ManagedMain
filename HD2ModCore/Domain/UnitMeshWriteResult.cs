namespace HD2ModCore.Domain;

// 作用：表示 Unit mesh 写回后的 TocData 与 GPU sidecar 二进制结果。
// Purpose: Represents serialized Unit TocData and GPU sidecar data produced by the Unit mesh writer.
public sealed record UnitMeshWriteResult(
	byte[] TocData,
	byte[] GpuData,
	byte[]? CompositeTocData = null,
	byte[]? CompositeGpuData = null);
