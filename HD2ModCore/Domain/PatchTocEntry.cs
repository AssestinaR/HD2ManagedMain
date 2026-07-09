namespace HD2ModCore.Domain;

// 作用：描述从 patch TOC 读出的原始资产 entry，包括后续重写资源所需的 offset/size 元数据。
// Purpose: Raw asset entry read from one patch TOC file before metadata enrichment, including offset/size metadata needed for resource rewriting.
public sealed record PatchTocEntry(
	AssetKey AssetKey,
	string SourceFilePath,
	string SourceFileName,
	ulong TocDataOffset = 0,
	ulong StreamOffset = 0,
	ulong GpuResourceOffset = 0,
	ulong Unknown1 = 0,
	ulong Unknown2 = 0,
	uint TocDataSize = 0,
	uint StreamSize = 0,
	uint GpuResourceSize = 0,
	uint Unknown3 = 0,
	uint Unknown4 = 0,
	uint EntryIndex = 0);