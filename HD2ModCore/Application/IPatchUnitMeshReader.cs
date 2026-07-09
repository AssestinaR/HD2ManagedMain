using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从 patch entry payload 中读取 Unit mesh 模型，并保留原始 payload 以便后续写回。
// Purpose: Reads a Unit mesh model from patch entry payload while preserving original payloads for later rewriting.
public interface IPatchUnitMeshReader
{
	ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, CancellationToken cancellationToken = default);

	ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, IReadOnlyList<PatchTocEntry> entries, CancellationToken cancellationToken = default);
}
