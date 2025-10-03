using System.Text.Json.Serialization;

namespace HD2ModCore.Domain;

// 作用：Profile 中的单条启用项（引用一个扁平 mod，并保存用户意图：启用、顺序、加入时间）。
// Purpose: A single enabled entry inside a Profile (references a flat mod and stores user intent: enabled, order and add time).
[method: JsonConstructor]
public sealed record ProfileEntry(
	ModNodeId NodeId,
	int LoadOrder,
	bool Enabled,
	DateTimeOffset AddedUtc)
{
	public ProfileEntry(ModNodeId nodeId, int loadOrder, bool enabled)
		: this(nodeId, loadOrder, enabled, DateTimeOffset.UtcNow)
	{
	}

	public int Order => LoadOrder;
}
