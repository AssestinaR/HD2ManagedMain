namespace HD2ModCore.Domain;

// 作用：表示某个原版 Archive 在投票推导中的命中情况（用于推导“该对象替换了哪些原版内容”）。
// Purpose: Represents how a base-game archive is voted/scored when deriving “what this object replaces”.
public sealed record ArchiveVote(
	string ArchiveId,
	string Category,
	string DisplayName,
	int Votes);
