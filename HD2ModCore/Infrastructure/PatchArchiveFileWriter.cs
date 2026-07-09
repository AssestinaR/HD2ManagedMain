using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：把 patch archive write plan 安全写入指定输出目录，避免直接覆盖源 patch 文件。
// Purpose: Safely writes patch archive plans to a chosen output directory, avoiding direct source patch overwrite.
public sealed class PatchArchiveFileWriter : IPatchArchiveFileWriter
{
	public async ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		PatchArchiveWritePlan plan,
		string outputDirectoryPath,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		if (string.IsNullOrWhiteSpace(outputDirectoryPath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(outputDirectoryPath));
		}

		var fullOutputDirectory = Path.GetFullPath(outputDirectoryPath);
		var sourcePatchPath = Path.GetFullPath(plan.SourcePatchFilePath);
		var sourceDirectory = Path.GetDirectoryName(sourcePatchPath);
		if (sourceDirectory is not null && fullOutputDirectory.Equals(Path.GetFullPath(sourceDirectory), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Patch archive output directory must be different from the source patch directory.");
		}

		Directory.CreateDirectory(fullOutputDirectory);
		var baseFileName = Path.GetFileName(sourcePatchPath);
		var tocPath = Path.Combine(fullOutputDirectory, baseFileName);
		var streamPath = tocPath + ".stream";
		var gpuPath = tocPath + ".gpu_resources";

		EnsureCanWrite(tocPath, overwriteExisting);
		EnsureCanWrite(streamPath, overwriteExisting);
		EnsureCanWrite(gpuPath, overwriteExisting);

		await File.WriteAllBytesAsync(tocPath, plan.TocFileData, cancellationToken).ConfigureAwait(false);
		if (plan.StreamFileData.Length > 0)
		{
			await File.WriteAllBytesAsync(streamPath, plan.StreamFileData, cancellationToken).ConfigureAwait(false);
		}
		if (plan.GpuResourceFileData.Length > 0)
		{
			await File.WriteAllBytesAsync(gpuPath, plan.GpuResourceFileData, cancellationToken).ConfigureAwait(false);
		}

		return new PatchArchiveFileWriteResult(
			fullOutputDirectory,
			tocPath,
			streamPath,
			gpuPath,
			plan.TocFileData.LongLength,
			plan.StreamFileData.LongLength,
			plan.GpuResourceFileData.LongLength);
	}

	private static void EnsureCanWrite(string path, bool overwriteExisting)
	{
		if (!overwriteExisting && File.Exists(path))
		{
			throw new IOException($"Output file already exists: {path}");
		}
	}
}
