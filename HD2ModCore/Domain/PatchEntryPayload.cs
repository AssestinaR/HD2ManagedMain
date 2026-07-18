namespace HD2ModCore.Domain;

// 作用：承载 Core 材质依赖闭包服务读取的单个 patch 条目及其 sidecar payload。
// Purpose: Carries one patch entry and sidecar payloads read by Core material dependency closure services.
public sealed record PatchEntryPayload(
	PatchTocEntry Entry,
	byte[] TocData,
	byte[] StreamData,
	byte[] GpuResourceData);