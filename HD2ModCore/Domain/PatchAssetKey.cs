namespace HD2ModCore.Domain;

// Purpose: Identifies an asset replacement inside a specific archive.
public readonly record struct PatchAssetKey(string ArchiveId, ulong TypeId, ulong FileId)
{
	public AssetKey AssetKey => new(TypeId, FileId);
}