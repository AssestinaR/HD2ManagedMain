using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：生成 ApplyPlan：按 Profile 顺序和 hex 分组将真实 patch 索引重新连续编号到游戏 data 目录。
// Purpose: Builds an ApplyPlan: renumbers real patch index files continuously per archive hex according to profile order.
public sealed class ApplyPlanner : IApplyPlanner
{
	private readonly IPatchFileNameParser _fileNameParser;

	public ApplyPlanner(IPatchFileNameParser fileNameParser)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
	}

	public ValueTask<ApplyPlan> BuildPlanAsync(
		Profile profile,
		LibrarySnapshot snapshot,
		PatchFileIndex patchIndex,
		string gameDataDirectory,
		CancellationToken cancellationToken = default)
	{
		if (profile is null)
		{
			throw new ArgumentNullException(nameof(profile));
		}
		if (snapshot is null)
		{
			throw new ArgumentNullException(nameof(snapshot));
		}
		if (patchIndex is null)
		{
			throw new ArgumentNullException(nameof(patchIndex));
		}
		if (string.IsNullOrWhiteSpace(gameDataDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameDataDirectory));
		}

		var gameData = Path.GetFullPath(gameDataDirectory);
		var ops = new List<ApplyOperation>();
		var issues = new List<CoreIssue>(patchIndex.Issues);

		foreach (var existing in EnumerateExistingPatchFiles(gameData))
		{
			cancellationToken.ThrowIfCancellationRequested();
			ops.Add(new ApplyOperation(
				ApplyOperationKind.DeletePatch,
				TargetPath: existing,
				SourcePath: null,
				ArchiveHex16: null,
				SourcePatchIndex: null,
				TargetPatchIndex: null,
				SidecarKind: null,
				NodeId: null));
		}

		var nextIndexByHex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var orderedEntries = profile.Entries
			.OrderBy(e => e.LoadOrder)
			.ThenBy(e => e.AddedUtc)
			.ThenBy(e => e.NodeId.Value)
			.ToList();

		foreach (var entry in orderedEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!snapshot.Nodes.ContainsKey(entry.NodeId))
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ProfileNodeMissing", $"Profile entry references a missing mod node: {entry.NodeId}", NodeId: entry.NodeId));
				continue;
			}

			if (!patchIndex.FilesByNode.TryGetValue(entry.NodeId, out var files) || files.Count == 0)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ProfileModHasNoPatchFiles", $"Profile mod has no patch files: {entry.NodeId}", NodeId: entry.NodeId));
				continue;
			}

			foreach (var hexGroup in files.GroupBy(f => f.ArchiveHex16, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
			{
				var targetBaseIndex = nextIndexByHex.TryGetValue(hexGroup.Key, out var next) ? next : 0;
				var sourceBaseIndexes = hexGroup
					.Where(f => f.SidecarKind == PatchSidecarKind.Base)
					.Select(f => f.NormalizedOrder)
					.Distinct()
					.OrderBy(x => x)
					.ToList();

				foreach (var sourceBaseIndex in sourceBaseIndexes)
				{
					foreach (var file in hexGroup.Where(f => f.NormalizedOrder == sourceBaseIndex).OrderBy(f => f.SidecarKind))
					{
						var targetPatchIndex = targetBaseIndex + sourceBaseIndex;
						var targetName = BuildFileName(file.ArchiveHex16, targetPatchIndex, file.SidecarKind);
						ops.Add(new ApplyOperation(
							ApplyOperationKind.DeployPatch,
							TargetPath: Path.Combine(gameData, targetName),
							SourcePath: file.FilePath,
							ArchiveHex16: file.ArchiveHex16,
							SourcePatchIndex: file.SourcePatchIndex,
							TargetPatchIndex: targetPatchIndex,
							SidecarKind: file.SidecarKind,
							NodeId: file.NodeId));
					}
				}

				nextIndexByHex[hexGroup.Key] = targetBaseIndex + sourceBaseIndexes.Count;
			}
		}

		return ValueTask.FromResult(new ApplyPlan(gameData, profile.Id, profile.Revision, DateTimeOffset.UtcNow, ops, issues));
	}

	private IEnumerable<string> EnumerateExistingPatchFiles(string gameData)
	{
		if (!Directory.Exists(gameData))
		{
			yield break;
		}

		foreach (var file in Directory.EnumerateFiles(gameData, "*", SearchOption.TopDirectoryOnly))
		{
			if (_fileNameParser.TryParse(Path.GetFileName(file), out _))
			{
				yield return file;
			}
		}
	}

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
}
