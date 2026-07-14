namespace HD2ModCore.Domain;

// 作用：模组库的持久化快照，包含对象树、profiles 与唯一活动配置。
// Purpose: Persisted library snapshot containing object trees, profiles and the sole active profile.
public sealed record LibrarySnapshot(
	int Version,
	DateTimeOffset SavedUtc,
	IReadOnlyDictionary<ModNodeId, ModNode> Nodes,
	IReadOnlyList<Profile> Profiles,
	ProfileId? ActiveProfileId = null);
