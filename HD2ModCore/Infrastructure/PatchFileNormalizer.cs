using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：将单个 mod 目录内同 hex 的 patch 文件按原编号规整为 patch_0..N，sidecar 跟随 base patch。
// Purpose: Normalizes patch files inside one mod directory to patch_0..N per archive hex, keeping sidecars with their base patch.
internal sealed class PatchFileNormalizer
{
	private readonly IPatchFileNameParser _parser;

	public PatchFileNormalizer(IPatchFileNameParser parser)
	{
		_parser = parser ?? throw new ArgumentNullException(nameof(parser));
	}

	public void NormalizeDirectory(string directoryPath, CancellationToken cancellationToken = default)
	{
		if (!Directory.Exists(directoryPath))
		{
			return;
		}

		var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
			.Select(path => new { Path = path, Name = Path.GetFileName(path), Parsed = TryParse(path) })
			.Where(x => x.Parsed is not null)
			.Select(x => (x.Path, x.Name, Info: x.Parsed!))
			.ToList();

		foreach (var hexGroup in files.GroupBy(x => x.Info.ArchiveHex16, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var orderedBaseIndexes = hexGroup
				.Where(x => x.Info.SidecarKind == PatchSidecarKind.Base)
				.Select(x => x.Info.PatchIndex)
				.Distinct()
				.OrderBy(x => x)
				.ToList();

			var sourceToNormalized = orderedBaseIndexes
				.Select((source, normalized) => new { source, normalized })
				.ToDictionary(x => x.source, x => x.normalized);

			foreach (var file in hexGroup.OrderBy(x => x.Info.PatchIndex).ThenBy(x => x.Info.SidecarKind))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!sourceToNormalized.TryGetValue(file.Info.PatchIndex, out var normalizedIndex))
				{
					continue;
				}

				var desiredName = BuildFileName(file.Info.ArchiveHex16, normalizedIndex, file.Info.SidecarKind);
				if (string.Equals(file.Name, desiredName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var desiredPath = Path.Combine(directoryPath, desiredName);
				MoveViaTemp(file.Path, desiredPath);
			}
		}
	}

	private PatchFileNameInfo? TryParse(string path)
		=> _parser.TryParse(Path.GetFileName(path), out var info) ? info : null;

	private static string BuildFileName(string hex, int index, PatchSidecarKind kind)
	{
		var suffix = kind switch
		{
			PatchSidecarKind.Base => string.Empty,
			PatchSidecarKind.Stream => ".stream",
			PatchSidecarKind.GpuResources => ".gpu_resources",
			_ => string.Empty,
		};

		return $"{hex}.patch_{index}{suffix}";
	}

	private static void MoveViaTemp(string sourcePath, string destinationPath)
	{
		if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var tempPath = sourcePath + ".hd2tmp-" + Guid.NewGuid().ToString("N");
		File.Move(sourcePath, tempPath);
		if (File.Exists(destinationPath))
		{
			File.Delete(destinationPath);
		}
		File.Move(tempPath, destinationPath);
	}
}