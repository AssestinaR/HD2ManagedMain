using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 Unit mesh 写回 API，把 UnitMeshModel 序列化为 Unit TocData 和 GPU sidecar。
// Purpose: Defines the Unit mesh writer API for serializing UnitMeshModel into Unit TocData and GPU sidecar data.
public interface IUnitMeshWriter
{
	UnitMeshWriteResult Write(UnitMeshModel model, ReadOnlySpan<byte> originalTocData, ReadOnlySpan<byte> originalCompositeTocData = default);
}
