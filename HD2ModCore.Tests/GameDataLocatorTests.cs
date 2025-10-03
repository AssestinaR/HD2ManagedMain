using HD2ModCore.Application;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证游戏 data 目录定位器在手动覆盖路径存在/不存在时的行为。
// Purpose: Verifies GameDataLocator behavior when manual override path exists/does not exist.
public sealed class GameDataLocatorTests
{
	private sealed class TestSettings : IGameDataSettings
	{
		public string? GameDataDirectoryOverride { get; init; }
	}

	[Fact]
	public async Task TryGetGameDataDirectoryAsync_OverrideValid_ReturnsOverride()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);

		try
		{
			var settings = new TestSettings { GameDataDirectoryOverride = tmp };
			var locator = new GameDataLocator(settings);
			var dir = await locator.TryGetGameDataDirectoryAsync();
			Assert.Equal(tmp, dir);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task TryGetGameDataDirectoryAsync_OverrideInvalid_FallsBackToAuto_AndMayReturnNull()
	{
		var settings = new TestSettings { GameDataDirectoryOverride = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
		var locator = new GameDataLocator(settings);
		var dir = await locator.TryGetGameDataDirectoryAsync();

		// In CI/dev machines Steam may or may not be installed; this test verifies it doesn't throw.
		Assert.True(dir is null || Directory.Exists(dir));
	}
}
