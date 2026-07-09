namespace HD2ModCore.Domain;

// 作用：描述从原版游戏 archive TOC 中定位到的资产 entry 及其 payload offset/size 元数据。
// Purpose: Describes an asset entry located in a vanilla game archive TOC with payload offset/size metadata.
public sealed record ArchiveTocEntry(
	AssetKey AssetKey,
	string ArchiveName,
	ulong TocDataOffset = 0,
	ulong StreamOffset = 0,
	ulong GpuResourceOffset = 0,
	uint TocDataSize = 0,
	uint StreamSize = 0,
	uint GpuResourceSize = 0,
	uint EntryIndex = 0);
