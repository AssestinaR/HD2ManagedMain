using System.Text.Json.Serialization;

namespace HD2ModCore.Domain;

// 作用：Profile 中的单条成员项（引用一个扁平 mod，并保存顺序和加入时间）。
// Purpose: A member entry inside a Profile, referencing a flat mod and storing order and add time.
[method: JsonConstructor]
public sealed record ProfileEntry(
	ModNodeId NodeId,
	int LoadOrder,
	DateTimeOffset AddedUtc)
{
	public ProfileEntry(ModNodeId nodeId, int loadOrder)
		: this(nodeId, loadOrder, DateTimeOffset.UtcNow)
	{
	}

	public int Order => LoadOrder;
}
