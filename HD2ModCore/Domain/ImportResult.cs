namespace HD2ModCore.Domain;

// 作用：一次将导入源落库后的结果（包含库快照与本次导入的根对象 id）。
// Purpose: Result of importing a source into the library (includes snapshot and the imported root node id).
public sealed record ImportResult(
	LibrarySnapshot Snapshot,
	ModNodeId ImportedRootId,
	string SourceDisplayName);
