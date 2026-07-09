using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Repairs outdated modded unit structures using current game unit data.
public interface IModUnitRepairService
{
	ValueTask<ModUnitRepairResult> RepairNodeAsync(
		ModNode node,
		string modsRootDirectory,
		string gameDataDirectory,
		ModUnitCompatibilityReport? compatibilityReport = null,
		CancellationToken cancellationToken = default);

	ValueTask<ModUnitRepairResult> RepairNodeAdvancedAsync(
		ModNode node,
		string modsRootDirectory,
		string gameDataDirectory,
		ModUnitCompatibilityReport? compatibilityReport = null,
		CancellationToken cancellationToken = default);
}