namespace HD2ModCore.Domain;

// 作用：模组库中的对象节点（对应一个目录），可包含 patch 文件组并且可具有子节点。
// Purpose: A mod library object node (represents a directory), containing patch groups and child nodes.
public sealed record ModNode(
	ModNodeId Id,
	string RelativePath,
	ModNodeMetadata Metadata,
	IReadOnlyList<PatchGroupKey> PatchGroups,
	IReadOnlyList<ModNodeId> Children);
