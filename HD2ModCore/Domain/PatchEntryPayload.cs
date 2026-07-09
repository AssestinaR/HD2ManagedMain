namespace HD2ModCore.Domain;

// 作用：保存从 patch TOC 与 sidecar 中提取出的单个资源 payload。
// Purpose: Holds one resource payload extracted from patch TOC data and sidecar files.
public sealed record PatchEntryPayload(
	PatchTocEntry Entry,
	byte[] TocData,
	byte[] StreamData,
	byte[] GpuResourceData);
