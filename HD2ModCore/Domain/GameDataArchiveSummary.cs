namespace HD2ModCore.Domain;

// 作用：表示持久化资产索引中的单个 archive 摘要，避免将完整 GameData facts 加载到内存。
// Purpose: Represents one archive summary from the persisted asset index without loading full GameData facts.
public sealed record GameDataArchiveSummary(
	string PackageName,
	string DisplayName,
	string Category,
	int EntryCount,
	string Status);
