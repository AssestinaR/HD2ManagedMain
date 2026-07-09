using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：读取 Helldivers 2 Unit 的 mesh/stream/material 摘要，作为后续 RawMesh 写回的入口。
// Purpose: Reads Helldivers 2 Unit mesh/stream/material summaries as the entry point for future RawMesh rewriting.
public interface IUnitMeshReader
{
	UnitMeshModel Read(ReadOnlySpan<byte> tocData, ReadOnlySpan<byte> gpuData, ReadOnlySpan<byte> compositeTocData = default, ReadOnlySpan<byte> compositeGpuData = default, UnitBoneNames? boneNames = null);
}