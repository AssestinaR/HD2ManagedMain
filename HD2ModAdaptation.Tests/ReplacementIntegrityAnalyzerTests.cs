using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies replacement coverage risk levels for shared resources.
public sealed class ReplacementIntegrityAnalyzerTests
{
	[Fact]
	public void Analyze_ReportsHighRiskForPartialSharedUnitCoverage()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 42);
		var items = new[] { Item("A", unit), Item("B", unit) };
		var group = Assert.Single(new ResourceReuseDetector().Detect(items, "fingerprint"));

		var finding = Assert.Single(new ReplacementIntegrityAnalyzer().Analyze(
			items,
			new[] { group with { ItemNames = new[] { "A", "B" } } },
			new[] { unit },
			"fingerprint"));

		Assert.Equal(ReplacementRiskLevel.Medium, finding.RiskLevel);
		Assert.Equal(new[] { "A", "B" }, finding.CoveredItems);
		Assert.Empty(finding.UncoveredItems);
	}

	[Fact]
	public void Analyze_ReportsOnlyModifiedGroups()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 1);
		var material = new AssetKey(MaterialDependencyResolver.MaterialTypeId, 2);
		var items = new[] { Item("A", unit, material), Item("B", unit, material) };
		var groups = new ResourceReuseDetector().Detect(items, "fingerprint");

		var findings = new ReplacementIntegrityAnalyzer().Analyze(items, groups, new[] { unit }, "fingerprint");

		var finding = Assert.Single(findings);
		Assert.Equal(unit, finding.SharedAsset);
	}

	private static GameItemResourceInfo Item(string name, params AssetKey[] assets) => new(
		name,
		"Armor",
		Array.Empty<string>(),
		assets.Select(asset => new ResourceDependencyFact(asset, "Unit", "items.archive", true, true)).ToArray(),
		Array.Empty<AssetKey>(),
		Array.Empty<PatchAnalysisIssue>());
}
