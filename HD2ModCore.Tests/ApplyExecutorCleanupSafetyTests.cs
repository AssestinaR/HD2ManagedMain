using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 ApplyExecutor 部署前会强制删除严格匹配的旧 patch，但不会触碰非 patch 文件。
// Purpose: Verifies ApplyExecutor force-deletes strict old patch files before deployment without touching non-patch files.
public sealed class ApplyExecutorCleanupSafetyTests
{
	[Fact]
	public async Task ExecuteAsync_DeletePatch_RemovesPatchButKeepsNonPatchFile()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var gameData = Path.Combine(root, "game", "data");
		Directory.CreateDirectory(gameData);

		var patch = Path.Combine(gameData, "9ba626afa44a3aa3.patch_0");
		var nonPatch = Path.Combine(gameData, "readme.txt");
		await File.WriteAllTextAsync(patch, "OLD");
		await File.WriteAllTextAsync(nonPatch, "KEEP");

		try
		{
			var plan = new ApplyPlan(gameData, null, DateTimeOffset.UtcNow, new[]
			{
				new ApplyOperation(ApplyOperationKind.DeletePatch, patch, null, null, null, null, null, null),
			}, Array.Empty<CoreIssue>());
			var exec = new ApplyExecutor();
			var result = await exec.ExecuteAsync(plan);

			Assert.True(result.Success);
			Assert.False(File.Exists(patch));
			Assert.True(File.Exists(nonPatch));
			Assert.Equal("KEEP", await File.ReadAllTextAsync(nonPatch));
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
