using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies Game Data archive indexing, lookup projection, and structured TOC failures.
public sealed class GameDataArchiveIndexerTests
{
	[Fact]
	public async Task BuildAsync_IndexesEntriesAndSupportsLookups()
	{
		var key = new AssetKey(PatchUnitMeshReader.UnitTypeId, 42);
		var resolver = new FakeResolver(new GameDataPackageToc(CreateToc(key), false));
		var indexer = new GameDataArchiveIndexer(_ => resolver);
		var input = new GameDataArchiveInput(".", new[] { "armor.archive" }, new Dictionary<string, GameDataArchiveMetadata>
		{
			["armor.archive"] = new("abc123", "Armor", "Equipment")
		});

		var result = await indexer.BuildAsync(input);

		var archive = Assert.Single(result.Archives);
		Assert.True(archive.IsIndexed);
		Assert.Equal("abc123", archive.ArchiveHex);
		Assert.Equal(key, Assert.Single(result.FindArchivesByAsset(key)).AssetKey);
		Assert.Equal(key, Assert.Single(result.FindEntriesByType(key.TypeId)).AssetKey);
		Assert.Equal(key, result.FindEntry("ARMOR.ARCHIVE", key)!.AssetKey);
	}

	[Fact]
	public async Task BuildAsync_ReportsMissingTocWithoutThrowing()
	{
		var result = await new GameDataArchiveIndexer(_ => new FakeResolver(null))
			.BuildAsync(new GameDataArchiveInput(".", new[] { "missing.archive" }));

		Assert.False(result.Archives.Single().IsIndexed);
		Assert.Equal("MissingArchiveToc", Assert.Single(result.Issues).Code);
	}

	[Fact]
	public void FindStreamLayouts_RequiresExactInlineAbiSignature()
	{
		var components = new[]
		{
			new GameDataStreamComponentFact(0, 2, 0, 0, 12),
			new GameDataStreamComponentFact(7, 35, 0, 0, 8),
			new GameDataStreamComponentFact(6, 28, 0, 0, 4)
		};
		var layout = new GameDataStreamLayoutFact("armor.archive", new AssetKey(PatchUnitMeshReader.UnitTypeId, 42), 3, 99, 10800438, 24, components);
		var index = new GameDataArchiveIndex(new GameDataArchiveInput("."), Array.Empty<GameDataArchiveFact>(), new[] { layout }, Array.Empty<PatchAnalysisIssue>(), DateTimeOffset.UtcNow, "test", "test");

		Assert.Equal(layout, Assert.Single(index.FindStreamLayouts(components, 24, requireSkinned: true)));
		Assert.Empty(index.FindStreamLayouts(components.Take(2).ToArray(), 20, requireSkinned: true));
	}

	private static byte[] CreateToc(AssetKey key)
	{
		var data = new byte[140];
		BitConverter.GetBytes(0xf0000011u).CopyTo(data, 0);
		BitConverter.GetBytes(1u).CopyTo(data, 8);
		BitConverter.GetBytes(key.FileId).CopyTo(data, 60);
		BitConverter.GetBytes(key.TypeId).CopyTo(data, 68);
		BitConverter.GetBytes(1u).CopyTo(data, 116);
		return data;
	}

	private sealed class FakeResolver(GameDataPackageToc? toc) : IGameDataPackageResolver
	{
		public ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default) => ValueTask.FromResult(toc);
		public ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default) => ValueTask.FromResult<byte[]?>(null);
		public ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
	}
}
