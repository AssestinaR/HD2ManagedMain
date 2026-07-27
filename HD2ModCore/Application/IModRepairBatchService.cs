using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Plans, stages, validates, backs up and commits conservative same-key repairs for multiple library Mods.
public interface IModRepairBatchService
{
    ValueTask<ModRepairBatchResult> RepairAsync(
        IReadOnlyList<ModNode> sourceNodes,
        string modsRootDirectory,
        string gameDataDirectory,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgressEvent>? progress = null,
        Guid? operationId = null);
}
