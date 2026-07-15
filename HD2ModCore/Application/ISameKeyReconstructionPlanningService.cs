using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Plans safe same-AssetKey target-shell reconstruction without writing or modifying a source mod.
public interface ISameKeyReconstructionPlanningService
{
	ValueTask<SameKeyReconstructionPlan> CreatePlanAsync(
		SameKeyReconstructionRequest request,
		CancellationToken cancellationToken = default);
}