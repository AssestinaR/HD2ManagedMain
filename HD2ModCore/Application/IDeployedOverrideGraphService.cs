using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Reconciles actual top-level Data patches with activation state and computes real AssetKey winners.
public interface IDeployedOverrideGraphService
{
	ValueTask<DeployedOverrideGraph> BuildAsync(
		string gameDataDirectory,
		CancellationToken cancellationToken = default);
}
