namespace HD2ModCore.Domain;

// Purpose: Lookup catalog built from cached community asset metadata files.
public sealed record AssetMetadataCatalog(
	IReadOnlyDictionary<string, ArchiveMetadata> Archives,
	IReadOnlyDictionary<ulong, FileMetadata> Files,
	IReadOnlyDictionary<ulong, TypeMetadata> Types)
{
	public static AssetMetadataCatalog Empty { get; } = new(
		new Dictionary<string, ArchiveMetadata>(StringComparer.OrdinalIgnoreCase),
		new Dictionary<ulong, FileMetadata>(),
		new Dictionary<ulong, TypeMetadata>());

	public ArchiveMetadata? FindArchive(string archiveId)
		=> Archives.TryGetValue(archiveId, out var metadata) ? metadata : null;

	public FileMetadata? FindFile(ulong fileId)
		=> Files.TryGetValue(fileId, out var metadata) ? metadata : null;

	public TypeMetadata? FindType(ulong typeId)
		=> Types.TryGetValue(typeId, out var metadata) ? metadata : null;
}