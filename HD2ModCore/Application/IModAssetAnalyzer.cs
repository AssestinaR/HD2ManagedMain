using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds asset-level summaries for mod nodes by scanning patch TOCs and enriching them with metadata.
public interface IModAssetAnalyzer
{
	ValueTask<ModAssetSummary> AnalyzeNodeAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}