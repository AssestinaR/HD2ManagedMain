using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies conservative reuse grouping and confidence assignment.
public sealed class ResourceReuseDetectorTests
{
	[Fact]
	public void Detect_GroupsSharedUnitAndMaterialSeparately()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 1);
		var material = new AssetKey(MaterialDependencyResolver.MaterialTypeId, 2);
		var items = new[]
		{
			Item("Armor A", unit, material),
			Item("Armor B", unit, material)
		};

		var groups = new ResourceReuseDetector().Detect(items, "fingerprint");

		Assert.Contains(groups, group => group.SharedAsset == unit && group.Level == ResourceReuseLevel.ExactUnitReuse && group.Confidence == ResourceReuseConfidence.High);
		Assert.Contains(groups, group => group.SharedAsset == material && group.Level == ResourceReuseLevel.MaterialReuse);
	}

	[Fact]
	public void Detect_DoesNotTreatTextureOnlyReuseAsModelReuse()
	{
		var texture = new AssetKey(MaterialDependencyResolver.TextureTypeId, 3);
		var groups = new ResourceReuseDetector().Detect(
			new[] { Item("A", texture), Item("B", texture) },
			"fingerprint");

		var group = Assert.Single(groups);
		Assert.Equal(ResourceReuseLevel.TextureOnlyReuse, group.Level);
		Assert.Equal(ResourceReuseConfidence.Low, group.Confidence);
		Assert.Contains("informational", group.Explanation, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Detect_IgnoresUnresolvedResources()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 4);
		var item = new GameItemResourceInfo("A", "Armor", Array.Empty<string>(), new[] { new ResourceDependencyFact(unit, "Unit", null, true, false) }, Array.Empty<AssetKey>(), Array.Empty<PatchAnalysisIssue>());

		Assert.Empty(new ResourceReuseDetector().Detect(new[] { item, item with { ItemName = "B" } }, "fingerprint"));
	}

	private static GameItemResourceInfo Item(string name, params AssetKey[] assets) => new(
		name,
		"Armor",
		Array.Empty<string>(),
		assets.Select(asset => new ResourceDependencyFact(asset, Kind(asset), "items.archive", true, true)).ToArray(),
		Array.Empty<AssetKey>(),
		Array.Empty<PatchAnalysisIssue>());

	private static string Kind(AssetKey asset) => asset.TypeId == PatchUnitMeshReader.UnitTypeId ? "Unit" : asset.TypeId == MaterialDependencyResolver.MaterialTypeId ? "Material" : "Texture";
}
