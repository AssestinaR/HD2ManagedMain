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
}

public interface IGameDataArchiveIndexer
{
	ValueTask<GameDataArchiveIndex> BuildAsync(GameDataArchiveInput input, CancellationToken cancellationToken = default);
}
