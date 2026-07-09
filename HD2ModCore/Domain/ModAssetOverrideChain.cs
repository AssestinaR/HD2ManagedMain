namespace HD2ModCore.Domain;

// Purpose: Ordered chain of mods replacing the same asset, with the final effective winner.
public sealed record ModAssetOverrideChain(
	PatchAssetKey Key,
	IReadOnlyList<ModAssetOverrideEntry> Entries)
{
	public ModAssetOverrideEntry Winner => Entries[^1];
}