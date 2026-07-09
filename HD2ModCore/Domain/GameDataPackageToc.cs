namespace HD2ModCore.Domain;

// Purpose: Describes TOC bytes resolved from a game data package and the entry table layout used by that package.
public sealed record GameDataPackageToc(byte[] Data, bool UsesSlimEntryOffset);
