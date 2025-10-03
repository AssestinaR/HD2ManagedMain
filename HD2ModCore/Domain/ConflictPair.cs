namespace HD2ModCore.Domain;

// 作用：两个对象节点之间的冲突结果（共享的 AssetKey 交集）。
// Purpose: Conflict result between two nodes (intersection of shared AssetKeys).
public sealed record ConflictPair(
	ModNodeId A,
	ModNodeId B,
	IReadOnlyList<AssetKey> SharedKeys);
