using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Validates complete patch groups and matches renamed groups by file content hashes.
public sealed class PatchStorageIntegrityValidator : IPatchStorageIntegrityValidator
{
	private readonly IPatchFileGroupFingerprintScanner _scanner;

	public PatchStorageIntegrityValidator(IPatchFileGroupFingerprintScanner scanner)
	{
		_scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
	}

	public async ValueTask<IReadOnlyList<PatchStorageIntegrityReport>> ValidateAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		PatchFileGroupFingerprintManifest? previousManifest,
		CancellationToken cancellationToken = default)
	{
		var current = await _scanner.ScanAsync(snapshot, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var reports = new List<PatchStorageIntegrityReport>();
		foreach (var node in snapshot.Nodes.Values)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var groups = current.TryGetValue(node.Id, out var found) ? found : Array.Empty<PatchFileGroupFingerprint>();
			var directory = Path.Combine(modsRootDirectory, node.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			var old = previousManifest?.Nodes.TryGetValue(node.Id, out var previousGroups) == true
				? previousGroups : Array.Empty<PatchFileGroupFingerprint>();

			if (!Directory.Exists(directory) || groups.Count == 0)
			{
				reports.Add(new PatchStorageIntegrityReport(node.Id, PatchStorageIntegrityStatus.Missing, new[] { "Mod 目录或全部 patch 组已丢失。" }, groups, false));
				continue;
			}

			var invalid = groups.Where(group => !IsComplete(group)).ToList();
			if (invalid.Count > 0)
			{
				reports.Add(new PatchStorageIntegrityReport(node.Id, PatchStorageIntegrityStatus.Corrupted, invalid.Select(group => $"patch 组不完整：{group.GroupName}").ToList(), groups, false));
				continue;
			}

			if (old.Count > 0 && TryMatchByContent(old, groups, out var renamed) && renamed)
			{
				reports.Add(new PatchStorageIntegrityReport(node.Id, PatchStorageIntegrityStatus.Renamed, new[] { "patch 组文件名已变化，但文件内容和组完整性未变化。" }, groups, false));
				continue;
			}

			var same = HaveSameContent(old, groups);
			reports.Add(new PatchStorageIntegrityReport(node.Id, same ? PatchStorageIntegrityStatus.Healthy : PatchStorageIntegrityStatus.Dirty, Array.Empty<string>(), groups, !same));
		}
		return reports;
	}

	public async ValueTask<IReadOnlyList<PatchStorageIntegrityReport>> ValidateAndRepairAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		PatchFileGroupFingerprintManifest? previousManifest,
		CancellationToken cancellationToken = default)
	{
		var reports = await ValidateAsync(snapshot, modsRootDirectory, previousManifest, cancellationToken).ConfigureAwait(false);
		foreach (var report in reports.Where(report => report.Status == PatchStorageIntegrityStatus.Renamed))
		{
			if (previousManifest?.Nodes.TryGetValue(report.NodeId, out var oldGroups) != true || oldGroups is null) continue;
			var directory = Path.Combine(modsRootDirectory, snapshot.Nodes[report.NodeId].RelativePath.Replace('/', Path.DirectorySeparatorChar));
			RepairNames(directory, oldGroups, report.CurrentGroups, cancellationToken);
		}

		return reports;
	}

	private static void RepairNames(
		string directory,
		IReadOnlyList<PatchFileGroupFingerprint> oldGroups,
		IReadOnlyList<PatchFileGroupFingerprint> currentGroups,
		CancellationToken cancellationToken)
	{
		var plans = new List<(string Source, string Temporary, string Target)>();
		var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var old in oldGroups)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var current = currentGroups.Single(group => HaveSameFiles(old, group));
			foreach (var file in current.EffectiveFileFingerprints)
			{
				var source = Path.Combine(directory, file.FileName);
				var target = Path.Combine(directory, BuildFileName(old.GroupName, file.SidecarKind));
				if (!usedTargets.Add(target)) throw new IOException($"patch 文件名修复目标冲突：{target}");
				if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
					plans.Add((source, Path.Combine(directory, $".hd2mod-rename-{Guid.NewGuid():N}.tmp"), target));
			}
		}

		foreach (var plan in plans)
		{
			if (!File.Exists(plan.Source)) throw new FileNotFoundException("patch 文件在修复期间消失。", plan.Source);
			File.Move(plan.Source, plan.Temporary);
		}
		try
		{
			foreach (var plan in plans)
			{
				if (File.Exists(plan.Target)) throw new IOException($"patch 文件名修复会覆盖现有文件：{plan.Target}");
				File.Move(plan.Temporary, plan.Target);
			}
		}
		catch
		{
			foreach (var plan in plans.Where(plan => File.Exists(plan.Temporary)))
				File.Move(plan.Temporary, plan.Source, overwrite: true);
			throw;
		}
	}

	private static string BuildFileName(string groupName, PatchSidecarKind sidecarKind)
		=> groupName + sidecarKind switch
		{
			PatchSidecarKind.Base => string.Empty,
			PatchSidecarKind.Stream => ".stream",
			PatchSidecarKind.GpuResources => ".gpu_resources",
			_ => throw new ArgumentOutOfRangeException(nameof(sidecarKind)),
		};

	private static bool IsComplete(PatchFileGroupFingerprint group)
	{
		var files = group.EffectiveFileFingerprints;
		return files.Count == 3 && files.Select(file => file.SidecarKind).Distinct().Count() == 3;
	}

	private static bool TryMatchByContent(IReadOnlyList<PatchFileGroupFingerprint> oldGroups, IReadOnlyList<PatchFileGroupFingerprint> currentGroups, out bool renamed)
	{
		renamed = false;
		if (oldGroups.Count != currentGroups.Count) return false;
		var matches = new HashSet<int>();
		foreach (var old in oldGroups)
		{
			var candidates = currentGroups.Select((current, index) => (current, index)).Where(pair => HaveSameFiles(old, pair.current)).ToList();
			if (candidates.Count != 1 || !matches.Add(candidates[0].index)) return false;
			renamed |= !string.Equals(old.GroupName, candidates[0].current.GroupName, StringComparison.OrdinalIgnoreCase) || !old.Files.SequenceEqual(candidates[0].current.Files, StringComparer.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool HaveSameContent(IReadOnlyList<PatchFileGroupFingerprint> oldGroups, IReadOnlyList<PatchFileGroupFingerprint> currentGroups)
		=> oldGroups.Count == currentGroups.Count && oldGroups.OrderBy(group => group.GroupName, StringComparer.OrdinalIgnoreCase).Select(group => group.ContentHash).SequenceEqual(currentGroups.OrderBy(group => group.GroupName, StringComparer.OrdinalIgnoreCase).Select(group => group.ContentHash), StringComparer.OrdinalIgnoreCase);

	private static bool HaveSameFiles(PatchFileGroupFingerprint left, PatchFileGroupFingerprint right)
		=> left.EffectiveFileFingerprints.Count == right.EffectiveFileFingerprints.Count && left.EffectiveFileFingerprints.OrderBy(file => file.SidecarKind).Zip(right.EffectiveFileFingerprints.OrderBy(file => file.SidecarKind), (a, b) => a.SidecarKind == b.SidecarKind && string.Equals(a.ContentHash, b.ContentHash, StringComparison.OrdinalIgnoreCase)).All(match => match);
}
