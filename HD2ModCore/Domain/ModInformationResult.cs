namespace HD2ModCore.Domain;

// 作用：将信息产品数据状态与本次生产诊断分开表达。
// Purpose: Separates returned data state from diagnostics for the current production attempt.
public sealed record ModInformationResult<T>(
	T? Data,
	ModInformationStatus Status,
	ModInformationKind Kind,
	string? Generation,
	IReadOnlyList<CoreIssue> Issues,
	bool WasCoalesced = false,
	bool RefreshFailed = false,
	bool CacheHit = false);