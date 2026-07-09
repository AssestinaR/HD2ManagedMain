namespace HD2ModCore.Domain;

// 作用：描述 patch-level Unit RawMesh 自动替换 dry-run 报告，便于后续 UI/CLI 展示候选、编辑、跳过与失败统计。
// Purpose: Describes a patch-level Unit RawMesh automatic replacement dry-run report for future UI/CLI summaries.
public sealed record PatchUnitMeshAutomationReport(
	PatchUnitMeshReplacementPlan ReplacementPlan,
	IReadOnlyList<PatchUnitMeshAutomationEntryReport> EntryReports)
{
	public PatchTocEntry SourceEntry => ReplacementPlan.SourceEntry;

	public int? RequestedSourceMeshInfoIndex => ReplacementPlan.RequestedSourceMeshInfoIndex;

	public int EntryCount => EntryReports.Count;

	public int CandidateCount => ReplacementPlan.CandidateCount;

	public int EditedEntryCount => EntryReports.Count(entry => entry.Status == PatchArchiveBatchEntryStatus.Edited);

	public int SkippedEntryCount => EntryReports.Count(entry => entry.Status == PatchArchiveBatchEntryStatus.Skipped);

	public int FailedEntryCount => EntryReports.Count(entry => entry.Status == PatchArchiveBatchEntryStatus.Failed);
}
