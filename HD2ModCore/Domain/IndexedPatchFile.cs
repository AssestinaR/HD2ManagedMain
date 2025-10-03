namespace HD2ModCore.Domain;

// 作用：真实磁盘中的一个 patch 文件索引项。
// Purpose: Indexed patch file found on disk.
public sealed record IndexedPatchFile(
	ModNodeId NodeId,
	string FilePath,
	string FileName,
	string ArchiveHex16,
	int SourcePatchIndex,
	int NormalizedOrder,
	PatchSidecarKind SidecarKind,
	long Length,
	DateTimeOffset LastWriteTimeUtc);