namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Describes one explicit keep, replace, add, or remove decision in a Patch workspace.
public enum PatchWorkspaceEntryChangeKind
{
	Keep = 0,
	Replace = 1,
	Add = 2,
	Remove = 3
}

public sealed record PatchWorkspaceEntryChange(
	AssetKey AssetKey,
	PatchWorkspaceEntryChangeKind Kind,
	PatchWorkspaceEntry? Entry = null)
{
	public static PatchWorkspaceEntryChange Keep(PatchWorkspaceEntry entry)
		=> new(entry.Metadata.AssetKey, PatchWorkspaceEntryChangeKind.Keep, entry);

	public static PatchWorkspaceEntryChange Replace(PatchWorkspaceEntry entry)
		=> new(entry.Metadata.AssetKey, PatchWorkspaceEntryChangeKind.Replace, entry);

	public static PatchWorkspaceEntryChange Add(PatchWorkspaceEntry entry)
		=> new(entry.Metadata.AssetKey, PatchWorkspaceEntryChangeKind.Add, entry);

	public static PatchWorkspaceEntryChange Remove(AssetKey assetKey)
		=> new(assetKey, PatchWorkspaceEntryChangeKind.Remove);
}
