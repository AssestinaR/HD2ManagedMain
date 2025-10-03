namespace HD2ModCore.Domain;

// 作用：用户的一套模组方案（启用列表 + 顺序），用于快速切换。
// Purpose: A user preset/profile (enabled list + load order) for fast switching.
public sealed record Profile(
	ProfileId Id,
	string Name,
	DateTimeOffset CreatedUtc,
	DateTimeOffset? ModifiedUtc,
	IReadOnlyList<ProfileEntry> Entries);
