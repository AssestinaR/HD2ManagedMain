namespace HD2ModCore.Domain;

// Purpose: Summarizes unit-structure compatibility for one mod node and feeds derived UI state.
public sealed record ModUnitCompatibilityReport(
	ModNodeId NodeId,
	ModUnitCompatibilityStatus Status,
	int PatchFileCount,
	int UnitCount,
	int InvalidModUnitCount,
	int OldLayoutCount,
	int VersionMismatchCount,
	int LodSizeMismatchCount,
	int MissingInGameCount,
	IReadOnlyList<ModUnitCompatibilityIssue> Issues)
{
	public bool HasHighConfidenceOutdated => Status is ModUnitCompatibilityStatus.Outdated or ModUnitCompatibilityStatus.Invalid;
	public bool CanRepair => Issues.Any(issue => issue.IsRepairable);
	public string BadgeText => Status switch
	{
		ModUnitCompatibilityStatus.Outdated => "已过时",
		ModUnitCompatibilityStatus.Invalid => "结构异常",
		ModUnitCompatibilityStatus.Unknown => "未检测",
		_ => string.Empty,
	};

	public string SummaryText
	{
		get
		{
			if (UnitCount <= 0)
			{
				return Status == ModUnitCompatibilityStatus.Unknown ? "Unit 结构检测不可用" : "未发现 unit 资源";
			}

			var parts = new List<string> { $"Unit: {UnitCount}" };
			if (InvalidModUnitCount > 0) parts.Add($"无效: {InvalidModUnitCount}");
			if (OldLayoutCount > 0) parts.Add($"旧结构: {OldLayoutCount}");
			if (VersionMismatchCount > 0) parts.Add($"版本不匹配: {VersionMismatchCount}");
			if (LodSizeMismatchCount > 0) parts.Add($"LOD 差异: {LodSizeMismatchCount}");
			if (MissingInGameCount > 0) parts.Add($"原版缺失: {MissingInGameCount}");
			return string.Join("，", parts);
		}
	}
}