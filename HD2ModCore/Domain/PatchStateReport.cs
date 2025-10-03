namespace HD2ModCore.Domain;

// 作用：游戏 data 或 mod 目录的 patch 状态扫描结果，用于启动检查与部署后验证。
// Purpose: Patch state scan result for game data or mod directories, used by startup checks and post-apply verification.
public sealed record PatchStateReport(
	string DirectoryPath,
	DateTimeOffset ScannedUtc,
	IReadOnlyList<PatchStateGroup> Groups,
	IReadOnlyList<CoreIssue> Issues);