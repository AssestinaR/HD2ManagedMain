namespace HD2ModCore.Domain;

// 作用：一次将 Profile 应用到游戏 data 目录的执行计划，包含清空旧 patch 与连续编号后的部署操作。
// Purpose: Execution plan for applying a Profile to the game data directory, including clearing old patches and normalized deployment operations.
public sealed record ApplyPlan(
	string GameDataDirectory,
	ProfileId? ProfileId,
	DateTimeOffset CreatedUtc,
	IReadOnlyList<ApplyOperation> Operations,
	IReadOnlyList<CoreIssue> Issues)
{
	public ApplyPlan(string gameDataDirectory, IReadOnlyList<ApplyOperation> operations)
		: this(gameDataDirectory, null, DateTimeOffset.UtcNow, operations, Array.Empty<CoreIssue>())
	{
	}
}
