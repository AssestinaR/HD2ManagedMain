namespace HD2ModCore.Domain;

// 作用：一次导入产生的对象树结构（包含根节点 id、所有节点索引，以及导入源信息）。
// Purpose: Object tree produced by a single import (root id, node index and source info).
public sealed record ImportedObjectTree(
	ModNodeId RootId,
	IReadOnlyDictionary<ModNodeId, ModNode> Nodes,
	string SourceDisplayName);
