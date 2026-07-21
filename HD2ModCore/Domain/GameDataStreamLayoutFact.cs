namespace HD2ModCore.Domain;

public sealed record GameDataStreamComponentFact(uint Type, uint Format, uint Index, ulong Unknown, uint Size);

public sealed record GameDataStreamLayoutFact(
	string ArchiveId,
	AssetKey UnitAssetKey,
	int StreamIndex,
	ulong ComponentInfoId,
	uint UnitVersion,
	uint VertexStride,
	IReadOnlyList<GameDataStreamComponentFact> Components,
	string LayoutSignature,
	bool IsSkinned);