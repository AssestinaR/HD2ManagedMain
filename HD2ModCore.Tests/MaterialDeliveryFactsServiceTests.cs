using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using AssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;

namespace HD2ModCore.Tests;

// Purpose: Verifies persisted Patch facts classify safe material delivery paths without payload re-reading.
public sealed class MaterialDeliveryFactsServiceTests
{
	private static readonly AssetKey Unit = new(0xe0a48d0be9a7453f, 1);
	private static readonly AssetKey Material = new(0xeac0b497876adedf, 2);
	private static readonly AssetKey Texture = new(0xcd4238c6a0c69e32, 3);

	[Fact]
	public async Task GetAsync_ClassifiesCompleteEmbeddedClosure()
	{
		var node = CreateNode("Embedded");
		var facts = Entry(node, [Unit, Material, Texture], [new PatchAssetReference(Unit, Material, PatchReferenceKind.UnitMaterial, 0), new PatchAssetReference(Material, Texture, PatchReferenceKind.MaterialTexture, 0)]);
		var result = await new MaterialDeliveryFactsService(new FakeInformationCenter(AdvancedFacts(facts)), new StoragePaths(Path.GetTempPath())).GetAsync(node.Id, Snapshot(node));

		Assert.Equal(MaterialDeliveryMode.EmbeddedComplete, result.Mode);
		Assert.True(result.CanRebuildAsWhole);
		Assert.Equal(1, result.EmbeddedMaterialCount);
	}

	[Fact]
	public async Task GetAsync_ResolvesSingleExternalMaterialProvider()
	{
		var model = CreateNode("Model");
		var materials = CreateNode("Materials");
		var entries = new[] {
			Entry(model, [Unit], [new PatchAssetReference(Unit, Material, PatchReferenceKind.UnitMaterial, 0)]),
			Entry(materials, [Material, Texture], [new PatchAssetReference(Material, Texture, PatchReferenceKind.MaterialTexture, 0)])
		};
		var center = new FakeInformationCenter(entries.Select(AdvancedFacts).ToDictionary(facts => facts.NodeId));
		var result = await new MaterialDeliveryFactsService(center, new StoragePaths(Path.GetTempPath())).GetAsync(model.Id, Snapshot(model, materials));

		Assert.Equal(MaterialDeliveryMode.ExternalResolved, result.Mode);
		Assert.True(result.CanRebuildModelOnly);
		var candidate = Assert.Single(result.Candidates);
		Assert.Equal(materials.Metadata.Name, candidate.Name);
		Assert.True(candidate.IsComplete);
	}

	private static PatchGroupAnalysisCacheEntry Entry(ModNode node, IReadOnlyCollection<AssetKey> assets, IReadOnlyCollection<PatchAssetReference> references)
	{
		var analysis = new PatchGroupAnalysis(
			new PatchGroupInput($"{node.Metadata.Name}.patch_0"),
			assets.Select(key => new PatchAssetFact(key, $"{node.Metadata.Name}.patch_0", 0, 0, 0, key.TypeId == 0xe0a48d0be9a7453f, false, key.TypeId == 0xeac0b497876adedf, key.TypeId == 0xcd4238c6a0c69e32)).ToArray(),
			references.ToArray(),
			Array.Empty<PatchAnalysisIssue>(),
			DateTimeOffset.UtcNow,
			"test");
		return new PatchGroupAnalysisCacheEntry(3, node.Id, node.RelativePath, Array.Empty<PatchAssetSourceFileFingerprint>(), DateTimeOffset.UtcNow, [analysis]);
	}
	private static AdvancedUnitAnalysisFacts AdvancedFacts(PatchGroupAnalysisCacheEntry entry)
		=> new(entry.NodeId, entry.RelativePath, "test", DateTimeOffset.UtcNow, entry.Analyses, []);

	private static ModNode CreateNode(string name) => new(ModNodeId.New(), name, new ModNodeMetadata(name, null, DateTimeOffset.UtcNow, null), Array.Empty<PatchGroupKey>(), Array.Empty<ModNodeId>());
	private static LibrarySnapshot Snapshot(params ModNode[] nodes) => new(1, DateTimeOffset.UtcNow, nodes.ToDictionary(node => node.Id), Array.Empty<Profile>());

}