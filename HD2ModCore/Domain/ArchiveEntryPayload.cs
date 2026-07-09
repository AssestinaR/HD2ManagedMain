namespace HD2ModCore.Domain;

// 作用：保存从原版游戏 archive entry 中提取出的单个资源 payload。
// Purpose: Holds one resource payload extracted from a vanilla game archive entry.
public sealed record ArchiveEntryPayload(
	ArchiveTocEntry Entry,
	byte[] TocData,
	byte[] StreamData,
	byte[] GpuResourceData);
