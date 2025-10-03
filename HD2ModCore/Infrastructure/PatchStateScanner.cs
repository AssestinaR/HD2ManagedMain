using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：扫描 patch 文件目录，报告分组连续性、sidecar 孤立与异常文件。
// Purpose: Scans patch directories and reports group continuity, orphan sidecars and abnormal files.
public sealed class PatchStateScanner : IPatchStateScanner
{
	private readonly IPatchFileNameParser _parser;

	public PatchStateScanner(IPatchFileNameParser parser)
	{
		_parser = parser ?? throw new ArgumentNullException(nameof(parser));
	}

	public ValueTask<PatchStateReport> ScanAsync(string directoryPath, bool recursive = false, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(directoryPath));
		}

		var full = Path.GetFullPath(directoryPath);
		var issues = new List<CoreIssue>();
		var parsed = new List<PatchFileNameInfo>();
		if (!Directory.Exists(full))
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "DirectoryMissing", $"Directory does not exist: {full}", full));
			return ValueTask.FromResult(new PatchStateReport(full, DateTimeOffset.UtcNow, Array.Empty<PatchStateGroup>(), issues));
		}

		foreach (var file in Directory.EnumerateFiles(full, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var name = Path.GetFileName(file);
			if (_parser.TryParse(name, out var info) && info is not null)
			{
				parsed.Add(info);
			}
		}

		var groups = new List<PatchStateGroup>();
		foreach (var group in parsed.GroupBy(x => x.ArchiveHex16, StringComparer.OrdinalIgnoreCase))
		{
			var baseIndexes = group.Where(x => x.SidecarKind == PatchSidecarKind.Base).Select(x => x.PatchIndex).Distinct().OrderBy(x => x).ToList();
			var streams = group.Where(x => x.SidecarKind == PatchSidecarKind.Stream).Select(x => x.PatchIndex).Distinct().OrderBy(x => x).ToList();
			var gpu = group.Where(x => x.SidecarKind == PatchSidecarKind.GpuResources).Select(x => x.PatchIndex).Distinct().OrderBy(x => x).ToList();
			var missing = new List<int>();
			if (baseIndexes.Count > 0)
			{
				for (var i = 0; i <= baseIndexes[^1]; i++)
				{
					if (!baseIndexes.Contains(i))
					{
						missing.Add(i);
					}
				}
			}

			foreach (var missingIndex in missing)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "PatchSequenceGap", $"Patch sequence has a gap: {group.Key}.patch_{missingIndex}"));
			}

			foreach (var sidecarIndex in streams.Concat(gpu).Distinct().Where(i => !baseIndexes.Contains(i)))
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "SidecarWithoutBase", $"Patch sidecar exists without base patch: {group.Key}.patch_{sidecarIndex}"));
			}

			groups.Add(new PatchStateGroup(group.Key, baseIndexes, missing, streams, gpu));
		}

		return ValueTask.FromResult(new PatchStateReport(full, DateTimeOffset.UtcNow, groups.OrderBy(g => g.ArchiveHex16, StringComparer.OrdinalIgnoreCase).ToList(), issues));
	}
}