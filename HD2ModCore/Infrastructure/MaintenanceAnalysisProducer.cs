using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从信息中心提供的 AssetInventory 结果生成可延期的维护兼容性分析。
// Purpose: Produces deferrable maintenance compatibility analysis from facts supplied by the information center.
public sealed class MaintenanceAnalysisProducer : IMaintenanceAnalysisProducer
{
	private readonly IModCompatibilityAnalyzer _analyzer;

	public MaintenanceAnalysisProducer(IModCompatibilityAnalyzer analyzer)
	{
		_analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
	}

	public async ValueTask<MaintenanceAnalysisFacts> ProduceAsync(ModNode node, ModContentFacts facts, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(facts);
		var assets = facts.PatchGroups.SelectMany(group => group.AssetKeys.Select(key => new PatchAssetEntry(new PatchAssetKey(group.Id.SourceArchiveHex, key.TypeId, key.FileId), group.Id.SourceArchiveHex, "Unknown", group.NormalizedOrder, group.Id.SourcePatchIndex, $"0x{key.FileId:x16}", $"0x{key.TypeId:x16}", AssetTypeCategory.Unknown, Array.Empty<string>(), Array.Empty<string>()))).ToArray();
		var summary = new ModAssetSummary(node.Id, node.Metadata.Name, assets, Array.Empty<string>(), Array.Empty<ModAssetTargetGroup>());
		var compatibility = await _analyzer.AnalyzeAsync(summary, cancellationToken).ConfigureAwait(false);
		return new MaintenanceAnalysisFacts(node.Id, node.RelativePath, facts.ContentGeneration, DateTimeOffset.UtcNow, compatibility, facts.Issues);
	}

}
