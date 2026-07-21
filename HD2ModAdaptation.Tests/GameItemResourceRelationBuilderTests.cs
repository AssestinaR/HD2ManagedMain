using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies conservative Item-to-resource relationship construction and ambiguity reporting.
public sealed class GameItemResourceRelationBuilderTests
{
	[Fact]
	public async Task BuildAsync_UsesExplicitUnitAndDirectAssets()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 1);
		var material = new AssetKey(MaterialDependencyResolver.MaterialTypeId, 2);
		var index = CreateIndex("armor.archive", unit, material);
		var item = new GameItemInput("Armor", "Equipment", new[] { "armor.archive" }, new[] { material }, new[] { unit });

		var result = await new GameItemResourceRelationBuilder().BuildAsync(index, new[] { item });

		var info = Assert.Single(result);
		Assert.Empty(info.Issues);
		Assert.Contains(info.DirectAssets, resource => resource.AssetKey == material && resource.ResourceKind == "Material");
		Assert.Contains(info.Resources, resource => resource.AssetKey == unit && resource.ResourceKind == "Unit");
	}

	[Fact]
	public async Task BuildAsync_ReportsAmbiguousImplicitUnitCandidates()
	{
		var index = CreateIndex("armor.archive", new AssetKey(PatchUnitMeshReader.UnitTypeId, 1), new AssetKey(PatchUnitMeshReader.UnitTypeId, 2));
		var item = new GameItemInput("Armor", "Equipment", new[] { "armor.archive" });

		var info = Assert.Single(await new GameItemResourceRelationBuilder().BuildAsync(index, new[] { item }));

		Assert.Contains(info.Issues, issue => issue.Code == "AmbiguousUnitCandidate");
		Assert.Equal(2, info.CandidateUnitAssets.Count);
	}

	private static GameDataArchiveIndex CreateIndex(string packageName, params AssetKey[] keys)
	{
		var entries = keys.Select((key, index) => new GameDataArchiveEntryFact(key, packageName, (uint)index, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0)).ToArray();
		var archive = new GameDataArchiveFact(packageName, null, null, null, false, entries, Array.Empty<PatchAnalysisIssue>());
		return new GameDataArchiveIndex(new GameDataArchiveInput("."), new[] { archive }, Array.Empty<GameDataStreamLayoutFact>(), Array.Empty<PatchAnalysisIssue>(), DateTimeOffset.UtcNow, "test", "test");
	}
}
