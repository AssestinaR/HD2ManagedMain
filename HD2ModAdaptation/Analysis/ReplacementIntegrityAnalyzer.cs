using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Reports conservative replacement coverage risks without expanding or modifying a Mod patch.
public sealed class ReplacementIntegrityAnalyzer : IReplacementIntegrityAnalyzer
{
	public IReadOnlyList<ReplacementCoverageFinding> Analyze(
		IReadOnlyList<GameItemResourceInfo> items,
		IReadOnlyList<ResourceReuseGroup> reuseGroups,
		IReadOnlyCollection<AssetKey> modifiedAssets,
		string sourceFingerprint)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(reuseGroups);
		ArgumentNullException.ThrowIfNull(modifiedAssets);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);

		var itemByName = items.ToDictionary(item => item.ItemName, StringComparer.Ordinal);
		var findings = new List<ReplacementCoverageFinding>();
		foreach (var group in reuseGroups.Where(group => modifiedAssets.Contains(group.SharedAsset)))
		{
			var covered = group.ItemNames
				.Where(name => itemByName.TryGetValue(name, out var item) && item.Resources.Any(resource => resource.AssetKey == group.SharedAsset && resource.IsResolved))
				.ToArray();
			var uncovered = group.ItemNames.Except(covered, StringComparer.Ordinal).ToArray();
			findings.Add(new ReplacementCoverageFinding(
				group.ItemNames.FirstOrDefault() ?? string.Empty,
				group.SharedAsset,
				group.Level,
				GetRisk(group.Level, uncovered.Length),
				covered,
				uncovered,
				GetExplanation(group, uncovered.Length > 0),
				sourceFingerprint));
		}

		return findings
			.OrderByDescending(finding => finding.RiskLevel)
			.ThenBy(finding => finding.ItemName, StringComparer.Ordinal)
			.ToArray();
	}

	private static ReplacementRiskLevel GetRisk(ResourceReuseLevel level, int uncoveredCount)
	{
		if (uncoveredCount > 0)
			return level switch
			{
				ResourceReuseLevel.ExactUnitReuse => ReplacementRiskLevel.High,
				ResourceReuseLevel.CompositeReuse or ResourceReuseLevel.MeshReuse => ReplacementRiskLevel.Medium,
				_ => ReplacementRiskLevel.Low
			};

		return level switch
		{
			ResourceReuseLevel.ExactUnitReuse => ReplacementRiskLevel.Medium,
			ResourceReuseLevel.CompositeReuse or ResourceReuseLevel.MeshReuse => ReplacementRiskLevel.Low,
			_ => ReplacementRiskLevel.Informational
		};
	}

	private static string GetExplanation(ResourceReuseGroup group, bool partialCoverage) => partialCoverage
		? $"Modified {group.Level} asset 0x{group.SharedAsset.FileId:x16} is shared by only part of the reuse group; related Items may become inconsistent."
		: $"Modified {group.Level} asset 0x{group.SharedAsset.FileId:x16} covers the complete detected reuse group; game compatibility still requires validation.";
}
