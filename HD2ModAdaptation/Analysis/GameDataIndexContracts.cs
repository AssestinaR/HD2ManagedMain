using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Defines neutral facts for the low-cost Game Data archive and AssetKey index.
public sealed record GameDataArchiveInput(
	string GameDataDirectory,
	IReadOnlyList<string>? PackageNames = null,
	IReadOnlyDictionary<string, GameDataArchiveMetadata>? MetadataByPackageName = null);

public sealed record GameDataArchiveMetadata(
	string? ArchiveHex = null,
	string? DisplayName = null,
	string? Category = null);

public sealed record GameDataArchiveIndexProgress(string Stage, int Current, int Total, string Item);

public sealed record GameDataArchiveEntryFact(
	AssetKey AssetKey,
	string PackageName,
	uint EntryIndex,
	ulong TocDataOffset,
	ulong StreamOffset,
	ulong GpuResourceOffset,
	uint TocDataSize,
	uint StreamSize,
	uint GpuResourceSize,
	ulong Unknown1,
	ulong Unknown2,
	uint Unknown3,
	uint Unknown4);

public sealed record GameDataStreamLayoutFact(
	string PackageName,
	AssetKey UnitAssetKey,
	int StreamIndex,
	ulong ComponentInfoId,
	uint UnitVersion,
	uint VertexStride,
	IReadOnlyList<GameDataStreamComponentFact> Components)
{
	public bool IsSkinned => Components.Any(component => component.Type is 6 or 7);

	public string LayoutSignature => string.Join(";", Components.Select(component => $"{component.Type}:{component.Format}:{component.Index}:{component.Unknown:x16}"));
}

public sealed record GameDataStreamComponentFact(uint Type, uint Format, uint Index, ulong Unknown, uint Size);

public sealed record GameDataArchiveFact(
	string PackageName,
	string? ArchiveHex,
	string? DisplayName,
	string? Category,
	bool UsesSlimEntryOffset,
	IReadOnlyList<GameDataArchiveEntryFact> Entries,
	IReadOnlyList<PatchAnalysisIssue> Issues)
{
	public bool IsIndexed => Issues.Count == 0;
}

public sealed record GameDataArchiveIndex(
	GameDataArchiveInput Input,
	IReadOnlyList<GameDataArchiveFact> Archives,
	IReadOnlyList<GameDataStreamLayoutFact> StreamLayouts,
	IReadOnlyList<PatchAnalysisIssue> Issues,
	DateTimeOffset BuiltUtc,
	string SchemaVersion,
	string ParserVersion)
{
	public IEnumerable<GameDataArchiveEntryFact> FindArchivesByAsset(AssetKey assetKey) =>
		Archives.SelectMany(archive => archive.Entries.Where(entry => entry.AssetKey == assetKey));

	public IEnumerable<GameDataArchiveEntryFact> FindEntriesByType(ulong typeId) =>
		Archives.SelectMany(archive => archive.Entries.Where(entry => entry.AssetKey.TypeId == typeId));

	public GameDataArchiveEntryFact? FindEntry(string packageName, AssetKey assetKey) =>
		Archives.FirstOrDefault(archive => string.Equals(archive.PackageName, packageName, StringComparison.OrdinalIgnoreCase))?.Entries.FirstOrDefault(entry => entry.AssetKey == assetKey);

	public IReadOnlyList<GameDataStreamLayoutFact> FindStreamLayouts(
		IReadOnlyList<GameDataStreamComponentFact> components,
		uint vertexStride,
		bool requireSkinned = false)
	{
		ArgumentNullException.ThrowIfNull(components);
		var signature = string.Join(";", components.Select(component => $"{component.Type}:{component.Format}:{component.Index}:{component.Unknown:x16}"));
		return StreamLayouts.Where(layout => layout.VertexStride == vertexStride
			&& (!requireSkinned || layout.IsSkinned)
			&& string.Equals(layout.LayoutSignature, signature, StringComparison.Ordinal)).ToArray();
	}
}

public interface IGameDataArchiveIndexer
{
	ValueTask<GameDataArchiveIndex> BuildAsync(GameDataArchiveInput input, IProgress<GameDataArchiveIndexProgress>? progress = null, CancellationToken cancellationToken = default);
}
