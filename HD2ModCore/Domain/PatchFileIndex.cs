namespace HD2ModCore.Domain;

// 作用：从磁盘真实扫描得到的临时 patch 索引，部署以它为准而不是完全相信 JSON。
// Purpose: Temporary patch index scanned from real files; deployment trusts this over persisted JSON facts.
public sealed record PatchFileIndex(
	DateTimeOffset BuiltUtc,
	IReadOnlyDictionary<ModNodeId, IReadOnlyList<IndexedPatchFile>> FilesByNode,
	IReadOnlyList<CoreIssue> Issues);