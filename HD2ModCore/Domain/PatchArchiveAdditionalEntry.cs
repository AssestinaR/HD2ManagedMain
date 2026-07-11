namespace HD2ModCore.Domain;

// 作用：描述需要追加进 patch archive 的额外资源 payload。
// Purpose: Describes an additional resource payload that should be appended to a rebuilt patch archive.
public sealed record PatchArchiveAdditionalEntry(
	AssetKey AssetKey,
	byte[] TocData,
	byte[] StreamData,
	byte[] GpuResourceData,
	ulong Unknown1 = 0,
	ulong Unknown2 = 0,
	uint Unknown3 = 0,
	uint Unknown4 = 0);