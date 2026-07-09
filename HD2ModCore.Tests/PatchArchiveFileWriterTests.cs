using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 PatchArchiveFileWriter 只向指定输出目录安全写入 archive write plan。
// Purpose: Verifies PatchArchiveFileWriter safely writes archive write plans only to a chosen output directory.
public sealed class PatchArchiveFileWriterTests
{
	[Fact]
	public async Task WriteAsync_NewOutputDirectory_WritesPlanFilesWithoutTouchingSource()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		var sourceDir = Path.Combine(tmp, "source");
		var outputDir = Path.Combine(tmp, "out");
		Directory.CreateDirectory(sourceDir);
		var sourcePatchPath = Path.Combine(sourceDir, "9ba626afa44a3aa3.patch_0");
		await File.WriteAllBytesAsync(sourcePatchPath, new byte[] { 1, 1, 1 });

		try
		{
			var plan = new PatchArchiveWritePlan(
				sourcePatchPath,
				new byte[] { 2, 3, 4 },
				new byte[] { 5, 6 },
				new byte[] { 7, 8, 9, 10 },
				Array.Empty<PatchTocEntry>(),
				Array.Empty<PatchArchiveEditPlacement>());
			var writer = new PatchArchiveFileWriter();

			var result = await writer.WriteAsync(plan, outputDir);

			Assert.Equal(new byte[] { 1, 1, 1 }, await File.ReadAllBytesAsync(sourcePatchPath));
			Assert.Equal(new byte[] { 2, 3, 4 }, await File.ReadAllBytesAsync(result.TocFilePath));
			Assert.Equal(new byte[] { 5, 6 }, await File.ReadAllBytesAsync(result.StreamFilePath));
			Assert.Equal(new byte[] { 7, 8, 9, 10 }, await File.ReadAllBytesAsync(result.GpuResourceFilePath));
			Assert.Equal(3, result.TocFileSize);
			Assert.Equal(2, result.StreamFileSize);
			Assert.Equal(4, result.GpuResourceFileSize);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task WriteAsync_SourceDirectory_Throws()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var sourcePatchPath = Path.Combine(tmp, "9ba626afa44a3aa3.patch_0");
		await File.WriteAllBytesAsync(sourcePatchPath, new byte[] { 1 });

		try
		{
			var plan = new PatchArchiveWritePlan(
				sourcePatchPath,
				new byte[] { 2 },
				Array.Empty<byte>(),
				Array.Empty<byte>(),
				Array.Empty<PatchTocEntry>(),
				Array.Empty<PatchArchiveEditPlacement>());
			var writer = new PatchArchiveFileWriter();

			await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(plan, tmp).AsTask());
			Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(sourcePatchPath));
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task WriteAsync_ExistingOutputWithoutOverwrite_ThrowsBeforeWriting()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		var sourceDir = Path.Combine(tmp, "source");
		var outputDir = Path.Combine(tmp, "out");
		Directory.CreateDirectory(sourceDir);
		Directory.CreateDirectory(outputDir);
		var sourcePatchPath = Path.Combine(sourceDir, "9ba626afa44a3aa3.patch_0");
		var outputPatchPath = Path.Combine(outputDir, "9ba626afa44a3aa3.patch_0");
		await File.WriteAllBytesAsync(sourcePatchPath, new byte[] { 1 });
		await File.WriteAllBytesAsync(outputPatchPath, new byte[] { 9 });

		try
		{
			var plan = new PatchArchiveWritePlan(
				sourcePatchPath,
				new byte[] { 2 },
				Array.Empty<byte>(),
				Array.Empty<byte>(),
				Array.Empty<PatchTocEntry>(),
				Array.Empty<PatchArchiveEditPlacement>());
			var writer = new PatchArchiveFileWriter();

			await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(plan, outputDir).AsTask());
			Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(outputPatchPath));
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}
}
