namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Holds Patch Entry metadata and header bytes without eagerly loading every sidecar payload.
public sealed record PatchWorkspaceIndex(
	string SourcePatchTocPath,
	IReadOnlyList<PatchTocEntry> Entries,
	byte[] HeaderTemplateTocData)
{
	public IReadOnlyDictionary<AssetKey, PatchTocEntry> ByAssetKey
		=> Entries.ToDictionary(entry => entry.AssetKey);
}