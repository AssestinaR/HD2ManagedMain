namespace HD2ModCore.Domain;

// 作用：模组库的持久化快照（包含对象树与 profiles 的集合）。
// Purpose: Persisted snapshot of the mod library (object trees and profiles).
public sealed record LibrarySnapshot(
	int Version,
	DateTimeOffset SavedUtc,
	IReadOnlyDictionary<ModNodeId, ModNode> Nodes,
	IReadOnlyList<Profile> Profiles);
