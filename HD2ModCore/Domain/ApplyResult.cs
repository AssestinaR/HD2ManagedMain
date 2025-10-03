namespace HD2ModCore.Domain;

// 作用：部署执行结果，包含每个操作结果、最终状态扫描与诊断信息。
// Purpose: Result of an apply execution, including per-operation results, final state scan and diagnostics.
public sealed record ApplyResult(
	bool Success,
	IReadOnlyList<ApplyOperationResult> Operations,
	PatchStateReport? StateReport,
	IReadOnlyList<CoreIssue> Issues);