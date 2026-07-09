namespace HD2ModCore.Domain;

// Purpose: Human-readable metadata for one file id from friendlynames.txt.
public sealed record FileMetadata(
	ulong FileId,
	string FriendlyName);