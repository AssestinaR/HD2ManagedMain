using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Joins SQLite Mod facts, Game Data targets and transient Profile projections for the advanced table.
public sealed class AdvancedModAssetQueryService : IAdvancedModAssetQueryService
{
	private readonly IReferenceGraphQueryIndex referenceIndex;
	private readonly IModInformationCenter informationCenter;
	private readonly string modsRootDirectory;
	private readonly IGameDataMappingFactsService mappingService;
	private readonly IAssetArchiveIndexService indexService;

	public AdvancedModAssetQueryService(IModInformationCenter informationCenter, HD2ModCore.Infrastructure.StoragePaths paths, IReferenceGraphQueryIndex referenceIndex, IGameDataMappingFactsService mappingService, IAssetArchiveIndexService indexService)
	{
		this.informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
		modsRootDirectory = (paths ?? throw new ArgumentNullException(nameof(paths))).ModsDirectory;
		this.referenceIndex = referenceIndex ?? throw new ArgumentNullException(nameof(referenceIndex));
		this.mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
		this.indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
	}

	public async ValueTask<IReadOnlyList<AdvancedModAssetRow>> QueryAsync(ModNodeId nodeId, LibrarySnapshot librarySnapshot, ProfileOverrideGraph? profileGraph, ProfileMaterialDiagnostics? diagnostics, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(librarySnapshot);
		var snapshot = await LoadAnalysisAsync(nodeId, librarySnapshot, cancellationToken).ConfigureAwait(false);
		if (snapshot is null || snapshot.Version <= 0 || snapshot.Analyses.Any(analysis => analysis.Depth is not (PatchAnalysisDepth.DependencyGraph or PatchAnalysisDepth.Full))) return Array.Empty<AdvancedModAssetRow>();
		var assets = snapshot.Analyses.SelectMany(analysis => analysis.Assets.Select(asset => (analysis, asset))).GroupBy(item => item.asset.AssetKey).ToArray();
		var domainKeys = assets.Select(group => new AssetKey(group.Key.TypeId, group.Key.FileId)).ToHashSet();
		var mapping = await mappingService.MapAsync(domainKeys, cancellationToken).ConfigureAwait(false);
		var unitKeys = domainKeys.Where(key => key.TypeId == 0xe0a48d0be9a7453f).ToHashSet();
		var partsByUnit = await indexService.GetUnitPartFactsAsync(unitKeys, cancellationToken).ConfigureAwait(false);
		var rows = new List<AdvancedModAssetRow>(assets.Length);
		foreach (var group in assets)
		{
			var key = new AssetKey(group.Key.TypeId, group.Key.FileId);
			var partSummary = partsByUnit.TryGetValue(key, out var parts) ? DescribeParts(parts) : "—";
			mapping.Assets.TryGetValue(key, out var mapped);
			var outgoing = snapshot.Analyses.SelectMany(analysis => analysis.References).Where(reference => reference.SourceAssetKey == group.Key).ToArray();
			var incoming = await referenceIndex.FindConsumerFactsAsync(group.Key, cancellationToken).ConfigureAwait(false);
			var chain = profileGraph?.AssetChains.FirstOrDefault(chain => chain.AssetKey == key);
			var winner = chain?.Winner;
			var nodeDiagnostics = diagnostics?.Items.Where(item => item.NodeId == nodeId && item.AssetKey == key).ToArray() ?? Array.Empty<ProfileMaterialDiagnostic>();
			var target = await BuildTargetSummaryAsync(key, mapped, incoming, librarySnapshot, cancellationToken).ConfigureAwait(false);
			var profileStatus = chain is null ? "未加入当前 Profile" : winner?.NodeId == nodeId ? "当前有效" : $"被 {winner?.ModName} 覆盖";
			rows.Add(new AdvancedModAssetRow(
				key,
				TypeName(key.TypeId),
				mapped?.FileDisplayName ?? $"0x{key.FileId:x16}",
				partSummary,
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

	private async ValueTask<PatchGroupAnalysisCacheEntry?> LoadAnalysisAsync(ModNodeId nodeId, LibrarySnapshot librarySnapshot, CancellationToken cancellationToken)
	{
		if (!librarySnapshot.Nodes.TryGetValue(nodeId, out var node)) return null;
		var result = await informationCenter.RequestAdvancedUnitAnalysisAsync(
			node,
			modsRootDirectory!,
			new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedAssetTable"),
			cancellationToken).ConfigureAwait(false);
		return result.Data is null ? null : new PatchGroupAnalysisCacheEntry(3, result.Data.NodeId, result.Data.RelativePath, [], result.Data.BuiltUtc, result.Data.Analyses);
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
			var unitConsumers = await referenceIndex.FindConsumerFactsAsync(material, cancellationToken).ConfigureAwait(false);
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

		private static string DescribeParts(IEnumerable<GameDataUnitPartFact> parts)
		{
			var visibleParts = parts
				.Where(part => part.IsVisualMesh && !part.IsLod && part.PartKind != UnitMeshPartKind.Unknown)
				.Select(part => $"{PartName(part.PartKind)}－{LayerName(part.Layer)}－{BodyVariantName(part.BodyVariant)}")
				.Distinct(StringComparer.Ordinal)
				.ToArray();
			return visibleParts.Length == 0 ? "—" : string.Join("，", visibleParts);
		}

		private static string PartName(UnitMeshPartKind kind) => kind switch
		{
			UnitMeshPartKind.Head => "头部",
			UnitMeshPartKind.Torso => "胸口",
			UnitMeshPartKind.Pelvis => "胯部",
			UnitMeshPartKind.LeftArm => "左臂",
			UnitMeshPartKind.RightArm => "右臂",
			UnitMeshPartKind.LeftLeg => "左腿",
			UnitMeshPartKind.RightLeg => "右腿",
			UnitMeshPartKind.LeftShoulder => "左肩甲",
			UnitMeshPartKind.RightShoulder => "右肩甲",
			UnitMeshPartKind.Accessory => "附件",
			_ => "未知"
		};

		private static string LayerName(UnitMeshPartLayer layer) => layer switch
		{
			UnitMeshPartLayer.Undergarment => "内部",
			UnitMeshPartLayer.Armor => "护甲",
			UnitMeshPartLayer.Accessory => "附件",
			UnitMeshPartLayer.Culling => "隐藏壳",
			UnitMeshPartLayer.Static => "静态",
			_ => "未分类"
		};

		private static string BodyVariantName(UnitMeshBodyVariant variant) => variant switch
		{
			UnitMeshBodyVariant.Slim => "纤细",
			UnitMeshBodyVariant.Stocky => "健壮",
			UnitMeshBodyVariant.Any => "通用",
			UnitMeshBodyVariant.Other => "其他体型",
			_ => "体型未知"
		};

	private static string TypeName(ulong typeId) => typeId switch
	{
		0xe0a48d0be9a7453f => "Unit",
		0xc4f0f4be7fb0c8d6 => "Composite Unit",
		0xeac0b497876adedf => "Material",
		0xcd4238c6a0c69e32 => "Texture",
		_ => $"0x{typeId:x16}"
	};
}