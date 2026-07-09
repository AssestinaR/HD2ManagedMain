namespace HD2ModCore.Domain;

// Purpose: Human-readable metadata for one game archive id from archivehashes.json.
public sealed record ArchiveMetadata(
	string ArchiveId,
	string Category,
	string DisplayName,
	int CategoryOrder = int.MaxValue,
	int ArchiveOrder = int.MaxValue);