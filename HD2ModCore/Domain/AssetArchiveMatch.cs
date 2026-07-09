namespace HD2ModCore.Domain;

// Purpose: Describes the current game archives that contain one asset key from a mod patch.
public sealed record AssetArchiveMatch(
	AssetKey AssetKey,
	IReadOnlyList<ArchiveMetadata> Archives)
{
	public bool Found => Archives.Count > 0;
}