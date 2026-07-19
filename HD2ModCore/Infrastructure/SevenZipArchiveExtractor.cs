using System.Diagnostics;

namespace HD2ModCore.Infrastructure;

// 作用：通过随管理器分发的 7-Zip 命令行运行时解压归档，隔离压缩格式解析并支持取消。
public sealed class SevenZipArchiveExtractor
{
	public async Task ExtractAsync(string archiveFilePath, string destinationDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(archiveFilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
		var archive = Path.GetFullPath(archiveFilePath);
		if (!File.Exists(archive)) throw new FileNotFoundException("Archive file not found.", archive);

		var executable = ResolveExecutablePath();
		Directory.CreateDirectory(destinationDirectory);
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executable,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(executable)!,
			}
		};
		process.StartInfo.ArgumentList.Add("x");
		process.StartInfo.ArgumentList.Add("-y");
		process.StartInfo.ArgumentList.Add("-bd");
		process.StartInfo.ArgumentList.Add($"-o{Path.GetFullPath(destinationDirectory)}");
		process.StartInfo.ArgumentList.Add(archive);

		if (!process.Start()) throw new InvalidOperationException("Unable to start bundled 7-Zip.");
		var outputTask = process.StandardOutput.ReadToEndAsync();
		var errorTask = process.StandardError.ReadToEndAsync();
		using var registration = cancellationToken.Register(() =>
		{
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
		});
		await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
		var output = await outputTask.ConfigureAwait(false);
		var error = await errorTask.ConfigureAwait(false);
		if (process.ExitCode != 0)
		{
			var detail = string.Join(Environment.NewLine, new[] { error, output }.Where(value => !string.IsNullOrWhiteSpace(value)));
			throw new InvalidDataException($"7-Zip extraction failed with exit code {process.ExitCode}.{Environment.NewLine}{detail}".Trim());
		}
	}

	private static string ResolveExecutablePath()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "third_party", "7zip", "7z.exe");
		if (!File.Exists(path)) throw new FileNotFoundException("Bundled 7-Zip executable is missing.", path);
		return path;
	}
}