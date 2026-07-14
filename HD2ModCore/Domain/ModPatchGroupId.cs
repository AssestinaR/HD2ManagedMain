namespace HD2ModCore.Domain;

// Purpose: Stable identity of one source patch group inside a flat internal mod.
public readonly record struct ModPatchGroupId(
	ModNodeId NodeId,
	string SourceArchiveHex,
	int SourcePatchIndex);
