namespace HD2ModCore.Domain;

// Purpose: Coarse player-facing archive overlap; it does not imply strict AssetKey competition.
public sealed record ProfileArchiveOverlap(
	string ArchiveId,
	string DisplayName,
	string Category,
	IReadOnlyList<ModNodeId> NodeIds);
