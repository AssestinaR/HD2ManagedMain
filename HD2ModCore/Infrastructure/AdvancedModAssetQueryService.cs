using System.Text.Json;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Joins SQLite Mod facts, Game Data targets and transient Profile projections for the advanced table.
public sealed class AdvancedModAssetQueryService : IAdvancedModAssetQueryService
{
	private readonly IModFactsStore factsStore;
	private readonly IGameDataMappingFactsService mappingService;

	public AdvancedModAssetQueryService(IModFactsStore factsStore, IGameDataMappingFactsService mappingService)
	{
		this.factsStore = factsStore ?? throw new ArgumentNullException(nameof(factsStore));
		this.mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
	}

	public async ValueTask<IReadOnlyList<AdvancedModAssetRow>> QueryAsync(ModNodeId nodeId, LibrarySnapshot librarySnapshot, ProfileOverrideGraph? profileGraph, ProfileMaterialDiagnostics? diagnostics, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(librarySnapshot);
		var snapshot = await factsStore.TryLoadAsync(nodeId, cancellationToken).ConfigureAwait(false);
		if (snapshot is null) return Array.Empty<AdvancedModAssetRow>();
		var assets = snapshot.Analyses.SelectMany(analysis => analysis.Assets.Select(asset => (analysis, asset))).GroupBy(item => item.asset.AssetKey).ToArray();
		var domainKeys = assets.Select(group => new AssetKey(group.Key.TypeId, group.Key.FileId)).ToHashSet();
		var mapping = await mappingService.MapAsync(domainKeys, cancellationToken).ConfigureAwait(false);
		var rows = new List<AdvancedModAssetRow>(assets.Length);
		foreach (var group in assets)
		{
			var key = new AssetKey(group.Key.TypeId, group.Key.FileId);
			mapping.Assets.TryGetValue(key, out var mapped);
			var outgoing = snapshot.Analyses.SelectMany(analysis => analysis.References).Where(reference => reference.SourceAssetKey == group.Key).ToArray();
			var incoming = await factsStore.FindConsumerFactsAsync(group.Key, cancellationToken).ConfigureAwait(false);
			var chain = profileGraph?.AssetChains.FirstOrDefault(chain => chain.AssetKey == key);
			var winner = chain?.Winner;
			var nodeDiagnostics = diagnostics?.Items.Where(item => item.NodeId == nodeId && item.AssetKey == key).ToArray() ?? Array.Empty<ProfileMaterialDiagnostic>();
			var target = await BuildTargetSummaryAsync(key, mapped, incoming, librarySnapshot, cancellationToken).ConfigureAwait(false);
			var profileStatus = chain is null ? "未加入当前 Profile" : winner?.NodeId == nodeId ? "当前有效" : $"被 {winner?.ModName} 覆盖";
			rows.Add(new AdvancedModAssetRow(
				key,
				TypeName(key.TypeId),
				mapped?.FileDisplayName ?? $"0x{key.FileId:x16}",
				target,
				$"引用 {outgoing.Length} / 被引用 {incoming.Count}",
				chain is null ? "无 Profile provider 链" : string.Join(" → ", chain.Entries.Select(entry => entry.ModName)),
				profileStatus,
				nodeDiagnostics.Length == 0 ? string.Empty : string.Join("；", nodeDiagnostics.Select(item => item.Summary).Distinct()),
				string.Join("，", group.Select(item => Path.GetFileName(item.analysis.Input.PatchTocFilePath)).Distinct(StringComparer.OrdinalIgnoreCase)),
				group.Sum(item => (long)item.asset.TocDataSize),
				group.Sum(item => (long)item.asset.StreamSize),
				group.Sum(item => (long)item.asset.GpuResourceSize)));
		}
		return rows.OrderBy(row => row.TypeName).ThenBy(row => row.AssetKey.FileId).ToArray();
	}

	private async ValueTask<string> BuildTargetSummaryAsync(
		AssetKey key,
		GameDataMappedAssetFact? mapped,
		IReadOnlyList<ModAssetConsumerFact> incoming,
		LibrarySnapshot librarySnapshot,
		CancellationToken cancellationToken)
	{
		var callers = key.TypeId switch
		{
			0xeac0b497876adedf => DescribeUnitConsumers(incoming.Where(consumer => consumer.Reference.Kind == PatchReferenceKind.UnitMaterial), librarySnapshot),
			0xcd4238c6a0c69e32 => await DescribeTextureConsumersAsync(incoming, librarySnapshot, cancellationToken).ConfigureAwait(false),
			_ => Array.Empty<string>(),
		};
		if (callers.Count != 0)
		{
			return $"Mod 引用：{string.Join("；", callers.Take(3))}" + (callers.Count > 3 ? $" +{callers.Count - 3}" : string.Empty);
		}
		if (mapped?.TargetArchives.Count > 0)
		{
			var archives = string.Join("，", mapped.TargetArchives.Take(2).Select(archive => archive.DisplayName));
			return archives + (mapped.TargetArchives.Count > 2 ? $" +{mapped.TargetArchives.Count - 2}" : string.Empty) + "（Game Data 映射）";
		}
		return incoming.Count > 0 ? $"{incoming.Count} 个直接调用方（Mod 引用）" : "未确认";
	}

	private async ValueTask<IReadOnlyList<string>> DescribeTextureConsumersAsync(IReadOnlyList<ModAssetConsumerFact> incoming, LibrarySnapshot librarySnapshot, CancellationToken cancellationToken)
	{
		var result = new List<string>();
		foreach (var materialConsumer in incoming.Where(consumer => consumer.Reference.Kind == PatchReferenceKind.MaterialTexture))
		{
			var material = materialConsumer.Reference.SourceAssetKey;
			var unitConsumers = await factsStore.FindConsumerFactsAsync(material, cancellationToken).ConfigureAwait(false);
			result.AddRange(DescribeUnitConsumers(unitConsumers.Where(consumer => consumer.Reference.Kind == PatchReferenceKind.UnitMaterial), librarySnapshot));
		}
		return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static IReadOnlyList<string> DescribeUnitConsumers(IEnumerable<ModAssetConsumerFact> consumers, LibrarySnapshot librarySnapshot)
		=> consumers
			.Select(consumer => librarySnapshot.Nodes.TryGetValue(consumer.NodeId, out var node)
				? $"{node.Metadata.Name} / Unit 0x{consumer.Reference.SourceAssetKey.FileId:x16}"
				: $"已移除 Mod / Unit 0x{consumer.Reference.SourceAssetKey.FileId:x16}")
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

	private static string TypeName(ulong typeId) => typeId switch
	{
		0xe0a48d0be9a7453f => "Unit",
		0xc4f0f4be7fb0c8d6 => "Composite Unit",
		0xeac0b497876adedf => "Material",
		0xcd4238c6a0c69e32 => "Texture",
		_ => $"0x{typeId:x16}"
	};
}