using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies payload-backed Unit and Material dependency graph construction.
public sealed class GameDataResourceGraphBuilderTests
{
	[Fact]
	public async Task BuildAsync_ResolvesUnitCompositeBoneAndMaterialTextureEdges()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 1);
		var composite = new AssetKey(PatchUnitMeshReader.CompositeUnitTypeId, 2);
		var bone = new AssetKey(PatchUnitMeshReader.BoneTypeId, 3);
		var material = new AssetKey(MaterialDependencyResolver.MaterialTypeId, 4);
		var texture = new AssetKey(MaterialDependencyResolver.TextureTypeId, 5);
		var entries = new[] { unit, composite, bone, material, texture }.Select((key, index) => Entry(key, (uint)index, key == unit ? 24u : key == material ? 152u : 0u)).ToArray();
		var archive = new GameDataArchiveFact("items.archive", null, null, null, false, entries, Array.Empty<PatchAnalysisIssue>());
		var index = new GameDataArchiveIndex(new GameDataArchiveInput("."), new[] { archive }, Array.Empty<PatchAnalysisIssue>(), DateTimeOffset.UtcNow, "test", "test");
		var resolver = new FakeResolver(new Dictionary<(string, ulong), byte[]>
		{
			[("items.archive", 0)] = ReferencePayload(3, 2),
			[("items.archive", 1)] = Array.Empty<byte>(),
			[("items.archive", 2)] = Array.Empty<byte>(),
			[("items.archive", 3)] = MaterialPayload(5),
			[("items.archive", 4)] = Array.Empty<byte>()
		});

		var graph = await new GameDataResourceGraphBuilder(_ => resolver, unitMaterialReader: _ => new[] { material.FileId }).BuildAsync(index, new[] { unit, material });

		Assert.Contains(graph.Edges, edge => edge.From == unit && edge.To == composite && edge.Relation == "CompositeReference" && edge.IsResolved);
		Assert.Contains(graph.Edges, edge => edge.From == unit && edge.To == bone && edge.Relation == "BoneReference" && edge.IsResolved);
		Assert.Contains(graph.Edges, edge => edge.From == material && edge.To == texture && edge.Relation == "TextureReference" && edge.IsResolved);
	}

	[Fact]
	public async Task BuildAsync_RecordsMissingDependencyAndTruncatedPayload()
	{
		var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 1);
		var missingComposite = new AssetKey(PatchUnitMeshReader.CompositeUnitTypeId, 99);
		var entry = Entry(unit, 0, 24);
		var index = CreateIndex(new[] { entry });
		var resolver = new FakeResolver(new Dictionary<(string, ulong), byte[]> { [("items.archive", 0)] = ReferencePayload(0, missingComposite.FileId) });

		var graph = await new GameDataResourceGraphBuilder(_ => resolver, unitMaterialReader: _ => Array.Empty<ulong>()).BuildAsync(index, new[] { unit });

		Assert.Contains(graph.Edges, edge => edge.To == missingComposite && !edge.IsResolved);
		Assert.Contains(graph.Issues, issue => issue.Code == "MissingResourcePayload" && issue.AssetKey == missingComposite);
	}

	[Fact]
	public async Task BuildAsync_HonorsCancellation()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var index = CreateIndex(Array.Empty<GameDataArchiveEntryFact>());

		await Assert.ThrowsAsync<OperationCanceledException>(() => new GameDataResourceGraphBuilder(_ => new FakeResolver(new Dictionary<(string, ulong), byte[]>())).BuildAsync(index, new[] { new AssetKey(PatchUnitMeshReader.UnitTypeId, 1) }, cancellation.Token).AsTask());
	}

	private static GameDataArchiveEntryFact Entry(AssetKey key, uint index, uint tocDataSize) => new(key, "items.archive", index, index, 0, 0, tocDataSize, 0, 0, 0, 0, 0, 0);
	private static GameDataArchiveIndex CreateIndex(IReadOnlyList<GameDataArchiveEntryFact> entries) => new(new GameDataArchiveInput("."), new[] { new GameDataArchiveFact("items.archive", null, null, null, false, entries, Array.Empty<PatchAnalysisIssue>()) }, Array.Empty<PatchAnalysisIssue>(), DateTimeOffset.UtcNow, "test", "test");
	private static byte[] ReferencePayload(ulong bone, ulong composite) { var data = new byte[24]; BitConverter.GetBytes(bone).CopyTo(data, 8); BitConverter.GetBytes(composite).CopyTo(data, 16); return data; }
	private static byte[] MaterialPayload(ulong texture) { var data = new byte[152]; BitConverter.GetBytes(1u).CopyTo(data, 64); BitConverter.GetBytes(texture).CopyTo(data, 140); return data; }

	private sealed class FakeResolver(IReadOnlyDictionary<(string, ulong), byte[]> payloads) : IGameDataPackageResolver
	{
		public ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataPackageToc?>(null);
		public ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default) => ValueTask.FromResult(payloads.TryGetValue((packageName, resourceOffset), out var data) ? (byte[]?)data : null);
		public ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<string>>(new[] { "items.archive" });
	}
}
