using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Detects whether modded unit assets use outdated or invalid game unit structures.
public interface IModUnitCompatibilityAnalyzer
{
	ValueTask<ModUnitCompatibilityReport> AnalyzeNodeAsync(
		ModNode node,
		string modsRootDirectory,
		string? gameDataDirectory,
		CancellationToken cancellationToken = default);
}