using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Projects already-built facts into concise Mod states without performing file-system or archive work.
public static class ModUserStatusProjector
{
	public static IReadOnlyDictionary<ModNodeId, ModUserStatus> Project(
		LibrarySnapshot snapshot,
		ProfileId? selectedProfileId,
		IReadOnlyDictionary<ModNodeId, ModContentFacts> content,
		ProfileOverrideGraph? expected,
		ProfileMaterialDiagnostics? materialDiagnostics,
		DeployedOverrideGraph? actual)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var selected = selectedProfileId is { } selectedId ? snapshot.Profiles.FirstOrDefault(profile => profile.Id == selectedId) : null;
		var active = snapshot.ActiveProfileId is { } activeId ? snapshot.Profiles.FirstOrDefault(profile => profile.Id == activeId) : null;
		var selectedIds = selected?.Entries.Select(entry => entry.NodeId).ToHashSet() ?? [];
		var activeIds = active?.Entries.Select(entry => entry.NodeId).ToHashSet() ?? [];
		var expectedIsCurrent = active is not null
			&& expected is not null
			&& expected.ProfileId == active.Id
			&& expected.ProfileRevision == active.Revision;
		var diagnosticsAreCurrent = active is not null
			&& materialDiagnostics is not null
			&& materialDiagnostics.ProfileId == active.Id
			&& materialDiagnostics.ProfileRevision == active.Revision;
		var actualBrokenNodeIds = actual?.Issues.Where(issue => issue.Severity == CoreIssueSeverity.Error && issue.NodeId is not null).Select(issue => issue.NodeId!.Value).ToHashSet() ?? [];
		var statuses = new Dictionary<ModNodeId, ModUserStatus>();
		foreach (var node in snapshot.Nodes.Values)
		{
			var inSelected = selectedIds.Contains(node.Id);
			var inActive = activeIds.Contains(node.Id);
			var contentIssues = content.TryGetValue(node.Id, out var facts) ? facts.Issues : Array.Empty<CoreIssue>();
			if (contentIssues.Any(issue => issue.Severity == CoreIssueSeverity.Error) || actualBrokenNodeIds.Contains(node.Id))
			{
				statuses[node.Id] = new ModUserStatus(node.Id, ModUserStatusKind.Broken, "文件异常", "Mod 文件或已部署副本需要检查。", inSelected, inActive);
				continue;
			}
			if (inActive)
			{
				var nodeDiagnostics = diagnosticsAreCurrent ? materialDiagnostics!.Items.Where(item => item.NodeId == node.Id).ToArray() : Array.Empty<ProfileMaterialDiagnostic>();
				var missing = nodeDiagnostics.Where(item => item.Kind is ProfileMaterialDiagnosticKind.MissingMaterial or ProfileMaterialDiagnosticKind.MissingTexture).ToArray();
				if (missing.Length != 0)
				{
					statuses[node.Id] = new ModUserStatus(node.Id, ModUserStatusKind.MissingDependency, "材质依赖缺失", string.Join("；", missing.Take(2).Select(item => item.Summary)) + (missing.Length > 2 ? $"；另有 {missing.Length - 2} 项" : string.Empty), inSelected, true);
					continue;
				}
				var unreachable = nodeDiagnostics.Where(item => item.Kind is ProfileMaterialDiagnosticKind.NoEffectiveUnitConsumer or ProfileMaterialDiagnosticKind.UnreachableResource).ToArray();
				if (unreachable.Length != 0)
				{
					statuses[node.Id] = new ModUserStatus(node.Id, ModUserStatusKind.NoEffectiveConsumer, "材质无有效调用方", string.Join("；", unreachable.Take(2).Select(item => item.Summary)) + (unreachable.Length > 2 ? $"；另有 {unreachable.Length - 2} 项" : string.Empty), inSelected, true);
					continue;
				}
				var coverage = expectedIsCurrent ? expected!.Coverages.FirstOrDefault(item => item.NodeId == node.Id) : null;
				statuses[node.Id] = coverage?.FullyOverridden == true
					? new ModUserStatus(node.Id, ModUserStatusKind.FullyOverridden, "已启用但失效", "此 Mod 的影响已被后续 Mod 完全覆盖。", inSelected, true)
					: coverage?.PartiallyOverridden == true
						? new ModUserStatus(node.Id, ModUserStatusKind.PartiallyOverridden, "已启用，局部覆盖", "此 Mod 的部分影响被后续 Mod 覆盖。", inSelected, true)
						: new ModUserStatus(node.Id, ModUserStatusKind.Enabled, "已启用", "已加入活动配置并等待或完成部署。", inSelected, true);
				continue;
			}
			statuses[node.Id] = inSelected
				? new ModUserStatus(node.Id, ModUserStatusKind.CurrentProfile, "当前配置", "已加入正在编辑的配置。", true, false)
				: new ModUserStatus(node.Id, ModUserStatusKind.Stored, "仅存储", "尚未加入正在编辑的配置。", false, false);
		}
		return statuses;
	}
}
