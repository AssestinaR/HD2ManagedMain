using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：按需生成并复用 Mod 的完整 Unit/材质引用缓存，避免各高级功能重复解析 Payload。
public interface IAdvancedModAnalysisService
{
	ValueTask<AdvancedModAnalysisState> GetStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
	ValueTask<AdvancedModAnalysisState> GetCachedStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
	ValueTask<AdvancedModAnalysisState> AnalyzeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<PatchGroupAnalysis>> GetRequiredAnalysesAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}

public sealed record AdvancedModAnalysisState(
	ModNodeId NodeId,
	bool IsReady,
	bool IsCurrent,
	DateTimeOffset? BuiltUtc,
	IReadOnlyList<CoreIssue> Issues);