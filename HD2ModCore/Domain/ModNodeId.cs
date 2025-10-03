namespace HD2ModCore.Domain;

// 作用：库中对象节点（目录节点）的稳定标识，用于引用与持久化。
// Purpose: Stable identifier for a library object node (directory node) for references and persistence.
public readonly record struct ModNodeId(Guid Value)
{
	public static ModNodeId New() => new(Guid.NewGuid());
}
