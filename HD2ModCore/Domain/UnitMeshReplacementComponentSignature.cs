namespace HD2ModCore.Domain;

// 作用：描述用于比较 Unit stream layout 的单个 vertex component 签名。
// Purpose: Describes one vertex component signature used to compare Unit stream layouts.
public sealed record UnitMeshReplacementComponentSignature(
	uint Type,
	uint Format,
	uint Index,
	uint Size);
