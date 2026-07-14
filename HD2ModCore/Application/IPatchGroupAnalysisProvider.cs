using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Acquires Adaptation-owned patch facts for one mod node.
public interface IPatchGroupAnalysisProvider
{
	ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeNodeAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
