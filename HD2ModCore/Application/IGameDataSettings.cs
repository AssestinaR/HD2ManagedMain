namespace HD2ModCore.Application;

// 作用：提供游戏 data 目录的用户设置输入（用于手动覆盖自动探测结果）。
// Purpose: Provides user settings input for the game data directory (manual override for auto-detection).
public interface IGameDataSettings
{
	string? GameDataDirectoryOverride { get; }
}
