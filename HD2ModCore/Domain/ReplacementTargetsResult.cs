namespace HD2ModCore.Domain;

// 作用：替换目标推导结果（默认展示 Top N，其余候选用于折叠显示与搜索）。
// Purpose: Replacement target derivation result (Top N for default display, plus remaining candidates for details/search).
public sealed record ReplacementTargetsResult(
	IReadOnlyList<ArchiveVote> Top,
	IReadOnlyList<ArchiveVote> Others);
