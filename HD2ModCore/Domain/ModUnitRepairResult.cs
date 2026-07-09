namespace HD2ModCore.Domain;

// Purpose: Summarizes a unit repair run for one mod node.
public sealed record ModUnitRepairResult(
	ModNodeId NodeId,
	bool Success,
	int PatchFileCount,
	int UpdatedPatchFileCount,
	int UpdatedUnitCount,
	int RemovedUnitCount,
	IReadOnlyList<CoreIssue> Issues)
{
	public string SummaryText
	{
		get
		{
			if (!Success)
			{
				return Issues.FirstOrDefault(i => i.Severity == CoreIssueSeverity.Error)?.Message ?? "Unit 修复失败。";
			}

			if (UpdatedUnitCount <= 0 && RemovedUnitCount <= 0)
			{
				return "没有需要修复的 unit。";
			}

			var parts = new List<string> { $"已修复 {UpdatedUnitCount} 个 unit" };
			if (RemovedUnitCount > 0) parts.Add($"移除 {RemovedUnitCount} 个无法匹配的 unit");
			if (UpdatedPatchFileCount > 0) parts.Add($"更新 {UpdatedPatchFileCount} 个 patch");
			return string.Join("，", parts);
		}
	}
}