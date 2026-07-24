using HD2ModCore.Domain;
using HD2ModCore.Application;

namespace HD2ModCore.Tests;

// 作用：验证信息中心对 FileFacts 请求的并发合并和业务取消隔离。
// Purpose: Verifies FileFacts request coalescing and caller-cancellation isolation.
public sealed class ModInformationCenterTests
{
	[Fact]
	public async Task RequestFileFactsAsync_CoalescesSameGeneration()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-information-center-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var node = new ModNode(new ModNodeId(Guid.NewGuid()), "mod", new ModNodeMetadata("Test", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), [], []);
			var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [node.Id] = node }, [], null);
			var producer = new CountingFileFactsProducer(new HD2ModCore.Infrastructure.PatchFileIndexBuilder(new HD2ModCore.Infrastructure.PatchFileNameParser()));
			var center = new HD2ModCore.Infrastructure.ModInformationCenter(
				producer,
				new ThrowingAssetInventoryProducer());
			var request = new ModInformationRequest(ModInformationKind.FileFacts, "Test");
			var first = center.RequestFileFactsAsync(snapshot, root, request).AsTask();
			var second = center.RequestFileFactsAsync(snapshot, root, request).AsTask();
			producer.Release();
			var results = await Task.WhenAll(first, second);
			Assert.Equal(1, producer.Count);
			Assert.Contains(results, result => result.WasCoalesced);
			await center.DisposeAsync();
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task RequestFileFactsAsync_ReturnsStructuredFailure()
	{
		var center = new HD2ModCore.Infrastructure.ModInformationCenter(
			new FailingFileFactsProducer(),
			new ThrowingAssetInventoryProducer());
		var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode>(), [], null);
		var result = await center.RequestFileFactsAsync(snapshot, Path.GetTempPath(), new ModInformationRequest(ModInformationKind.FileFacts, "Test"));
		Assert.Equal(ModInformationStatus.Failed, result.Status);
		Assert.Contains(result.Issues, issue => issue.Code == "FileFactsProductionFailed");
		await center.DisposeAsync();
	}

	[Fact]
	public async Task RequestAssetInventoryAsync_ProductionFailureReturnsLatestStaleCache()
	{
		var node = new ModNode(new ModNodeId(Guid.NewGuid()), "mod", new ModNodeMetadata("Test", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), [], []);
		var stale = new ModContentFacts(node.Id, node.RelativePath, "old-generation", DateTimeOffset.UtcNow, [], []);
		var cache = new LatestOnlyInformationCache(stale);
		var center = new HD2ModCore.Infrastructure.ModInformationCenter(
			new FailingFileFactsProducer(),
			new ThrowingAssetInventoryProducer(),
			informationCache: cache);

		var result = await center.RequestAssetInventoryAsync(node, Path.GetTempPath(), new ModInformationRequest(ModInformationKind.AssetInventory, "Test", "new-generation", true));

		Assert.Equal(ModInformationStatus.Stale, result.Status);
		Assert.True(result.RefreshFailed);
		Assert.Equal("old-generation", result.Generation);
		Assert.Same(stale, result.Data);
		Assert.Contains(result.Issues, issue => issue.Code == "AssetInventoryProductionFailed");
		await center.DisposeAsync();
	}

	[Fact]
	public async Task RequestAssetInventoryAsync_DoesNotPublishAfterNodeInvalidation()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-information-center-invalidation-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var node = new ModNode(new ModNodeId(Guid.NewGuid()), "mod", new ModNodeMetadata("Test", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), [], []);
			var producer = new BlockingAssetInventoryProducer(node);
			var cache = new RecordingInformationCache();
			var center = new HD2ModCore.Infrastructure.ModInformationCenter(
				new FailingFileFactsProducer(), producer, informationCache: cache);
			var request = center.RequestAssetInventoryAsync(node, root, new ModInformationRequest(ModInformationKind.AssetInventory, "Test", "generation", true)).AsTask();
			await producer.Started.Task;

			await center.InvalidateNodeAsync(node.Id);
			producer.Release();
			var result = await request;

			Assert.NotEqual(ModInformationStatus.Fresh, result.Status);
			Assert.Empty(cache.SavedGenerations);
			await center.DisposeAsync();
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private sealed class CountingFileFactsProducer(HD2ModCore.Application.IPatchFileIndexBuilder builder) : HD2ModCore.Application.IModFileFactsProducer
	{
		public int Count { get; private set; }
		private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public void Release() => _release.TrySetResult();
		public async ValueTask<PatchFileIndex> ProduceAsync(LibrarySnapshot snapshot, string root, CancellationToken cancellationToken = default)
		{
			Count++;
			await _release.Task.WaitAsync(cancellationToken);
			return await builder.BuildAsync(snapshot, root, cancellationToken);
		}
	}

	private sealed class ThrowingAssetInventoryProducer : HD2ModCore.Application.IModContentFactsService
	{
		public ValueTask<ModContentFacts> GetNodeFactsAsync(ModNode node, string root, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetLibraryFactsAsync(LibrarySnapshot snapshot, string root, IReadOnlySet<ModNodeId>? nodeIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class FailingFileFactsProducer : HD2ModCore.Application.IModFileFactsProducer
	{
		public ValueTask<PatchFileIndex> ProduceAsync(LibrarySnapshot snapshot, string root, CancellationToken cancellationToken = default)
			=> throw new InvalidOperationException("test failure");
	}

	private sealed class BlockingAssetInventoryProducer(ModNode node) : HD2ModCore.Application.IModContentFactsService
	{
		public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async ValueTask<ModContentFacts> GetNodeFactsAsync(ModNode requestedNode, string root, CancellationToken cancellationToken = default)
		{
			Started.TrySetResult();
			await _release.Task;
			return new ModContentFacts(node.Id, node.RelativePath, "new-generation", DateTimeOffset.UtcNow, [], []);
		}

		public ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetLibraryFactsAsync(LibrarySnapshot snapshot, string root, IReadOnlySet<ModNodeId>? nodeIds = null, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public void Release() => _release.TrySetResult();
	}

	private sealed class RecordingInformationCache : IModInformationCache
	{
		public List<string> SavedGenerations { get; } = [];
		public ValueTask<T?> TryLoadAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, CancellationToken cancellationToken = default) => ValueTask.FromResult<T?>(default);
		public ValueTask<ModInformationCacheEntry<T>?> TryLoadLatestAsync<T>(ModInformationKind kind, ModNodeId nodeId, CancellationToken cancellationToken = default) => ValueTask.FromResult<ModInformationCacheEntry<T>?>(default);
		public ValueTask SaveAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, T data, CancellationToken cancellationToken = default)
		{
			SavedGenerations.Add(generation);
			return ValueTask.CompletedTask;
		}
		public ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
	}

	private sealed class LatestOnlyInformationCache(ModContentFacts stale) : IModInformationCache
	{
		public ValueTask<T?> TryLoadAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<T?>(default);

		public ValueTask<ModInformationCacheEntry<T>?> TryLoadLatestAsync<T>(ModInformationKind kind, ModNodeId nodeId, CancellationToken cancellationToken = default)
			=> kind == ModInformationKind.AssetInventory && typeof(T) == typeof(ModContentFacts)
				? ValueTask.FromResult<ModInformationCacheEntry<T>?>(new ModInformationCacheEntry<T>(stale.ContentGeneration, (T)(object)stale, stale.BuiltUtc))
				: ValueTask.FromResult<ModInformationCacheEntry<T>?>(default);

		public ValueTask SaveAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, T data, CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;

		public ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;
	}
}
