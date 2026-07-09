namespace HD2ModCore.Domain;

// Purpose: Human-readable metadata for one type id from typehash.txt.
public sealed record TypeMetadata(
	ulong TypeId,
	string Name,
	AssetTypeCategory Category);