namespace HD2ModCore.Domain;

// 作用：同一个 hex 分组下的 patch 状态。
// Purpose: Patch state for a single archive hex group.
public sealed record PatchStateGroup(
	string ArchiveHex16,
	IReadOnlyList<int> BaseIndexes,
	IReadOnlyList<int> MissingIndexes,
	IReadOnlyList<int> StreamIndexes,
	IReadOnlyList<int> GpuResourceIndexes);