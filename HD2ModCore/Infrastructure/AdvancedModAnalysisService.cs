using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：兼容旧高级分析接口，并转发至统一信息中心。
public sealed class AdvancedModAnalysisService : IAdvancedModAnalysisService
{
	private readonly IModInformationCenter _informationCenter;

	public AdvancedModAnalysisService(IModInformationCenter informationCenter)
	{
		_informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
	}

	public async ValueTask<AdvancedModAnalysisState> GetStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var result = await _informationCenter.RequestAdvancedUnitAnalysisAsync(
			node,
			modsRootDirectory,
			new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedModAnalysisService"),
			cancellationToken).ConfigureAwait(false);
		return new AdvancedModAnalysisState(node.Id, result.Data is not null, result.Status is ModInformationStatus.Fresh or ModInformationStatus.Cached, result.Data?.BuiltUtc, result.Issues);
	}

	public async ValueTask<AdvancedModAnalysisState> GetCachedStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var result = await _informationCenter.RequestAdvancedUnitAnalysisAsync(node, modsRootDirectory,
			new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedModAnalysisService", RequireFresh: false), cancellationToken).ConfigureAwait(false);
		return new AdvancedModAnalysisState(node.Id, result.Data is not null, result.Data is not null, result.Data?.BuiltUtc, result.Issues);
	}

	public async ValueTask<AdvancedModAnalysisState> AnalyzeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		// “模型解析”是显式用户刷新：始终绕过持久缓存重建高级 Unit 分析。
		var result = await _informationCenter.RequestAdvancedUnitAnalysisAsync(node, modsRootDirectory,
			new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedModAnalysisService", RequireFresh: true), cancellationToken).ConfigureAwait(false);
		return new AdvancedModAnalysisState(node.Id, result.Data is not null, result.Status is ModInformationStatus.Fresh or ModInformationStatus.Cached, result.Data?.BuiltUtc, result.Issues);
	}

	public async ValueTask<IReadOnlyList<PatchGroupAnalysis>> GetRequiredAnalysesAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var result = await _informationCenter.RequestAdvancedUnitAnalysisAsync(node, modsRootDirectory, new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedModAnalysisService"), cancellationToken).ConfigureAwait(false);
		if (result.Data is null) throw new InvalidOperationException("请先执行高级分析以建立完整 Unit 和材质引用缓存。");
		return result.Data.Analyses;
	}

}