using System.Collections.Concurrent;
using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：维护由信息产品构建的进程内跨 Mod 资产/引用索引，不参与基础部署。
// Purpose: Maintains an in-process cross-Mod asset/reference index without participating in deployment.
public sealed class ModDataIndex : IModDataIndex
{
	private const int SchemaVersion = 1;
	private readonly ConcurrentDictionary<ModNodeId, IReadOnlyList<ModDataIndexEntry>> _entries = new();
	private readonly ConcurrentDictionary<ModNodeId, IReadOnlyList<ModDataIndexEntry>> _providers = new();
	private readonly ConcurrentDictionary<ModNodeId, IReadOnlyList<ModDataIndexEntry>> _consumers = new();
	private readonly string? _persistencePath;
	private readonly object _persistenceLock = new();
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private volatile ModDataIndexStatus _status = ModDataIndexStatus.Unavailable;

	public ModDataIndex()
	{
	}

	public ModDataIndex(StoragePaths paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		_persistencePath = Path.Combine(paths.IndexDirectory, "mod-data-index.json");
		Load();
	}

	public void Update(ModContentFacts inventory)
	{
		_providers[inventory.NodeId] = inventory.PatchGroups.SelectMany(group => group.AssetKeys.Select(key => new ModDataIndexEntry(inventory.NodeId, inventory.RelativePath, group.Id.SourceArchiveHex, group.Id.SourcePatchIndex, new AssetKey(key.TypeId, key.FileId), "Provider"))).ToArray();
		if (_status == ModDataIndexStatus.Unavailable) _status = ModDataIndexStatus.Partial;
		RebuildNode(inventory.NodeId);
	}

	public void Update(ReferenceGraphFacts graph)
	{
		_consumers[graph.NodeId] = graph.Analyses.SelectMany(analysis => analysis.References.Select(reference => new ModDataIndexEntry(graph.NodeId, graph.RelativePath, Path.GetFileName(analysis.Input.PatchTocFilePath), 0, new AssetKey(reference.TargetAssetKey.TypeId, reference.TargetAssetKey.FileId), "Consumer"))).ToArray();
		if (_status == ModDataIndexStatus.Unavailable) _status = ModDataIndexStatus.Partial;
		RebuildNode(graph.NodeId);
	}

	public ValueTask<IReadOnlyList<ModDataIndexEntry>> FindProvidersAsync(AssetKey assetKey, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<ModDataIndexEntry>>(_entries.Values.SelectMany(value => value).Where(entry => entry.Relation == "Provider" && entry.AssetKey == assetKey).ToArray());

	public ValueTask<IReadOnlyList<ModDataIndexEntry>> FindConsumersAsync(AssetKey assetKey, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<ModDataIndexEntry>>(_entries.Values.SelectMany(value => value).Where(entry => entry.Relation == "Consumer" && entry.AssetKey == assetKey).ToArray());

	public ValueTask<ModDataIndexSummary> GetAssetRelationSummaryAsync(IReadOnlyCollection<AssetKey> assetKeys, ModNodeId? excludedNodeId = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(assetKeys);
		if (_status == ModDataIndexStatus.Unavailable) return ValueTask.FromResult(new ModDataIndexSummary(ModDataIndexStatus.Unavailable, 0, 0));
		var requested = assetKeys.ToHashSet();
		var counts = _entries.Values
			.SelectMany(value => value)
			.Where(entry => (!excludedNodeId.HasValue || entry.NodeId != excludedNodeId.Value) && requested.Contains(entry.AssetKey))
			.GroupBy(entry => entry.Relation)
			.ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
		return ValueTask.FromResult(new ModDataIndexSummary(
			_status,
			counts.GetValueOrDefault("Provider"),
			counts.GetValueOrDefault("Consumer")));
	}

	public ValueTask<ModDataIndexEntry?> ResolveFinalProviderAsync(AssetKey assetKey, Profile profile, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		var order = profile.Entries
			.OrderBy(entry => entry.LoadOrder)
			.ThenBy(entry => entry.AddedUtc)
			.ThenBy(entry => entry.NodeId.Value)
			.Select((entry, index) => (entry.NodeId, Index: index))
			.ToDictionary(item => item.NodeId, item => item.Index);
		var result = _entries.Values
			.SelectMany(value => value)
			.Where(entry => entry.Relation == "Provider" && entry.AssetKey == assetKey && order.ContainsKey(entry.NodeId))
			.OrderBy(entry => order[entry.NodeId])
			.ThenBy(entry => entry.PatchIndex)
			.LastOrDefault();
		return ValueTask.FromResult(result);
	}

	public ValueTask RemoveNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		_entries.TryRemove(nodeId, out _);
		_providers.TryRemove(nodeId, out _);
		_consumers.TryRemove(nodeId, out _);
		if (_entries.IsEmpty) _status = ModDataIndexStatus.Unavailable;
		Persist();
		return ValueTask.CompletedTask;
	}

	private void RebuildNode(ModNodeId nodeId)
	{
		var entries = _providers.TryGetValue(nodeId, out var providers) ? providers : Array.Empty<ModDataIndexEntry>();
		if (_consumers.TryGetValue(nodeId, out var consumers)) entries = entries.Concat(consumers).ToArray();
		_entries[nodeId] = entries;
		Persist();
	}

	private void Load()
	{
		if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath)) return;
		try
		{
			var envelope = JsonSerializer.Deserialize<PersistedIndex>(File.ReadAllText(_persistencePath), JsonOptions);
			if (envelope is null || envelope.SchemaVersion != SchemaVersion) return;
			foreach (var group in envelope.Entries.GroupBy(entry => entry.NodeId))
				_entries[group.Key] = group.ToArray();
			foreach (var group in envelope.Entries.Where(entry => entry.Relation == "Provider").GroupBy(entry => entry.NodeId))
				_providers[group.Key] = group.ToArray();
			foreach (var group in envelope.Entries.Where(entry => entry.Relation == "Consumer").GroupBy(entry => entry.NodeId))
				_consumers[group.Key] = group.ToArray();
			_status = envelope.Entries.Count == 0 ? ModDataIndexStatus.Unavailable : ModDataIndexStatus.Ready;
		}
		catch (IOException) { }
		catch (JsonException) { }
	}

	private void Persist()
	{
		if (string.IsNullOrWhiteSpace(_persistencePath)) return;
		lock (_persistenceLock)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_persistencePath)!);
				var temporary = _persistencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
				File.WriteAllText(temporary, JsonSerializer.Serialize(new PersistedIndex(SchemaVersion, DateTimeOffset.UtcNow, _entries.Values.SelectMany(value => value).ToArray()), JsonOptions));
				File.Move(temporary, _persistencePath, true);
			}
			catch (IOException) { }
		}
	}

	private sealed record PersistedIndex(int SchemaVersion, DateTimeOffset BuiltUtc, IReadOnlyList<ModDataIndexEntry> Entries);
}
