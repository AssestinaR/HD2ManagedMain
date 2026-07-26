namespace HD2ModCore.Domain;

// 作用：描述信息中心一次真实生产任务的开始，供管理器展示后台进度。
public sealed record ModInformationProductionStarted(
	string Source,
	ModInformationKind Kind,
	ModNodeId? NodeId,
	string? Generation,
	DateTimeOffset StartedAt,
	string OperationKey);
