namespace HD2ModCore.Domain;

// 作用：描述 patch-level Unit RawMesh 自动替换 dry-run 中单个 entry 的报告项。
// Purpose: Describes one entry report item in a patch-level Unit RawMesh automatic replacement dry-run.
public sealed record PatchUnitMeshAutomationEntryReport(
	PatchTocEntry Entry,
	PatchArchiveBatchEntryStatus Status,
	string Reason,
	PatchUnitMeshReplacementCandidate? Candidate = null,
	Exception? Exception = null)
{
	public bool HasCandidate => Candidate is not null;
}
