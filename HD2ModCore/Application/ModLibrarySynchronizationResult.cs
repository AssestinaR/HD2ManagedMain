using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：描述一次模组库文件系统对账结果。
// Purpose: Describes the result of one mod-library filesystem reconciliation.
public sealed record ModLibrarySynchronizationResult(
	LibrarySnapshot Snapshot,
	IReadOnlySet<ModNodeId> AddedNodeIds,
	IReadOnlySet<ModNodeId> ChangedNodeIds,
	IReadOnlySet<ModNodeId> MissingNodeIds,
	bool FilesystemChanged);
