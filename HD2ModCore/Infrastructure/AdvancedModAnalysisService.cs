using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：兼容旧高级分析接口，并转发至统一信息中心。
public sealed class AdvancedModAnalysisService : IAdvancedModAnalysisService
{
	private readonly IModInformationCenter _informationCenter;
	private readonly IModInformationCache _informationCache;

	public AdvancedModAnalysisService(IModInformationCenter informationCenter, IModInformationCache informationCache)
	{
		_informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
		_informationCache = informationCache ?? throw new ArgumentNullException(nameof(informationCache));
	}

	public async ValueTask<AdvancedModAnalysisState> GetStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var generation = AdvancedUnitAnalysisProducer.ComputeGeneration(node, modsRootDirectory);
		var cached = await _informationCache.TryLoadAsync<AdvancedUnitAnalysisFacts>(ModInformationKind.AdvancedUnitAnalysis, node.Id, generation, cancellationToken).ConfigureAwait(false);
		return new AdvancedModAnalysisState(node.Id, cached is not null, cached is not null, cached?.BuiltUtc, cached?.Issues ?? Array.Empty<CoreIssue>());
	}

	public async ValueTask<AdvancedModAnalysisState> AnalyzeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var result = await _informationCenter.RequestAdvancedUnitAnalysisAsync(node, modsRootDirectory, new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedModAnalysisService", RequireFresh: true), cancellationToken).ConfigureAwait(false);
		return new AdvancedModAnalysisState(node.Id, result.Data is not null, result.Status is ModInformationStatus.Fresh or ModInformationStatus.Cached, result.Data?.BuiltUtc, result.Issues);
	}

	public async ValueTask<IReadOnlyList<PatchGroupAnalysis>> GetRequiredAnalysesAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var result = await _informationCenter.RequestAdvancedUnitAnalysisAsync(node, modsRootDirectory, new ModInformationRequest(ModInformationKind.AdvancedUnitAnalysis, "AdvancedModAnalysisService"), cancellationToken).ConfigureAwait(false);
		if (result.Data is null) throw new InvalidOperationException("请先执行高级分析以建立完整 Unit 和材质引用缓存。");
		return result.Data.Analyses;
	}

}