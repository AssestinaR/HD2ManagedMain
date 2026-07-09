using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Analyzes ordered mod lists to determine asset override chains and final effective replacements.
public interface IModAssetOverrideAnalyzer
{
	ValueTask<ModAssetOverrideAnalysis> AnalyzeAsync(
		IReadOnlyList<ProfileEntry> orderedEntries,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}