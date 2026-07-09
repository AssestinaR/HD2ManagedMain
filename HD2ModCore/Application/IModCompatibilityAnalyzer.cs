using HD2ModCore.Domain;

namespace HD2ModCore.Application;

public interface IModCompatibilityAnalyzer
{
	ValueTask<ModCompatibilityReport> AnalyzeAsync(ModAssetSummary summary, CancellationToken cancellationToken = default);
}