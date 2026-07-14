namespace HD2ModCore.Domain;

// Purpose: Simple player-facing status card for one Mod, derived from content, expected and actual facts.
public sealed record ModUserStatus(
	ModNodeId NodeId,
	ModUserStatusKind Kind,
	string Title,
	string Summary,
	bool IsInSelectedProfile,
	bool IsInActiveProfile);
