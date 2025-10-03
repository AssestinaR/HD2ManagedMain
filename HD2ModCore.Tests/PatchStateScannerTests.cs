using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 patch 状态扫描器能发现编号缺口和孤立 sidecar。
// Purpose: Verifies the patch state scanner detects numbering gaps and orphan sidecars.
public sealed class PatchStateScannerTests
{
	[Fact]
	public async Task ScanAsync_ReportsGapsAndOrphanSidecars()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_0"), "");
			File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_2"), "");
			File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_3.stream"), "");

			var report = await new PatchStateScanner(new PatchFileNameParser()).ScanAsync(root);

			var group = Assert.Single(report.Groups);
			Assert.Equal(new[] { 0, 2 }, group.BaseIndexes);
			Assert.Equal(new[] { 1 }, group.MissingIndexes);
			Assert.Contains(report.Issues, i => i.Code == "PatchSequenceGap");
			Assert.Contains(report.Issues, i => i.Code == "SidecarWithoutBase");
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}