using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 patch 文件名解析器对合法/非法输入的行为。
// Purpose: Verifies patch filename parser behavior for valid/invalid inputs.
public sealed class PatchFileNameParserTests
{
	[Theory]
	[InlineData("9ba626afa44a3aa3.patch_0", "9ba626afa44a3aa3", 0, Domain.PatchSidecarKind.Base)]
	[InlineData("9ba626afa44a3aa3.patch_0.stream", "9ba626afa44a3aa3", 0, Domain.PatchSidecarKind.Stream)]
	[InlineData("9ba626afa44a3aa3.patch_1.gpu_resources", "9ba626afa44a3aa3", 1, Domain.PatchSidecarKind.GpuResources)]
	[InlineData("9BA626AFA44A3AA3.patch_12", "9ba626afa44a3aa3", 12, Domain.PatchSidecarKind.Base)]
	public void TryParse_Valid_ReturnsExpected(string name, string hex, int n, Domain.PatchSidecarKind kind)
	{
		var parser = new PatchFileNameParser();
		Assert.True(parser.TryParse(name, out var info));
		Assert.NotNull(info);
		Assert.Equal(hex, info!.ArchiveHex16);
		Assert.Equal(n, info.PatchIndex);
		Assert.Equal(kind, info.SidecarKind);
		Assert.Equal(Path.GetFileName(name), info.FullFileName);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("not_a_patch")]
	[InlineData("9ba626afa44a3aa3.patch_-1")]
	[InlineData("9ba626afa44a3aa3.patch_A")]
	[InlineData("9ba626afa44a3aa3.patch_0.extra")]
	[InlineData("9ba626afa44a3aa.patch_0")]
	[InlineData("9ba626afa44a3aa3.patcH_0")]
	public void TryParse_Invalid_ReturnsFalse(string name)
	{
		var parser = new PatchFileNameParser();
		Assert.False(parser.TryParse(name, out var info));
		Assert.Null(info);
	}
}
