using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Removes persisted derived data for a node during import rollback or explicit cleanup.
public interface IModDerivedDataCleanup
{
	ValueTask DeleteAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
}
