using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：基于资产键交集进行冲突检测（对象 vs 对象、或对象与 Profile 应用集）。
// Purpose: Detects conflicts by intersecting asset keys (node vs node, or node vs applied profile set).
public interface IConflictDetector
{
	ValueTask<IReadOnlyList<ConflictPair>> DetectNodeConflictsAsync(
		IReadOnlyList<ModNodeId> nodeIds,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
