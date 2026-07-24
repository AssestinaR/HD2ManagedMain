namespace HD2ModCore.Domain;

// 作用：记录一次信息中心请求的可审计结果。
// Purpose: Records an auditable outcome for one information-center request.
public sealed record ModInformationDiagnostic(
	string Source,
	ModInformationKind Kind,
	ModNodeId? NodeId,
	string? Generation,
	DateTimeOffset StartedUtc,
	DateTimeOffset CompletedUtc,
	bool CacheHit,
	bool WasCoalesced,
	ModInformationStatus Status,
	IReadOnlyList<CoreIssue> Issues);