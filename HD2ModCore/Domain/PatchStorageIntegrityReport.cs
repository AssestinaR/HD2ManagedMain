namespace HD2ModCore.Domain;

public enum PatchStorageIntegrityStatus
{
	Healthy,
	Renamed,
	Dirty,
	Corrupted,
	Missing,
}

// Purpose: Describes the storage integrity result for one library node.
public sealed record PatchStorageIntegrityReport(
	ModNodeId NodeId,
	PatchStorageIntegrityStatus Status,
	IReadOnlyList<string> Messages,
	IReadOnlyList<PatchFileGroupFingerprint> CurrentGroups,
	bool RequiresDerivedRefresh)
{
	public bool IsRemovable => Status is PatchStorageIntegrityStatus.Corrupted or PatchStorageIntegrityStatus.Missing;
}
