namespace HD2ModCore.Domain;

// 作用：Core 返回给 UI 或日志的统一诊断信息。
// Purpose: Unified diagnostic issue returned by Core to UI or logs.
public sealed record CoreIssue(
	CoreIssueSeverity Severity,
	string Code,
	string Message,
	string? FilePath = null,
	ModNodeId? NodeId = null,
	string? ExceptionMessage = null);