using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：按 patch TOC entry 的 offset/size 从 patch 与 sidecar 文件中读取资源 payload。
// Purpose: Reads resource payload bytes from patch and sidecar files using patch TOC entry offset/size metadata.
public interface IPatchEntryPayloadReader
{
	ValueTask<PatchEntryPayload> ReadPayloadAsync(PatchTocEntry entry, CancellationToken cancellationToken = default);
}
