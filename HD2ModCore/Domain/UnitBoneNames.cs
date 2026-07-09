namespace HD2ModCore.Domain;

// 作用：保存 Unit Bones 资源中的骨骼 hash 与名称，供 SDK 风格 mesh 命名使用。
// Purpose: Holds bone hashes and names from a Unit Bones resource for SDK-style mesh naming.
public sealed record UnitBoneNames(
	IReadOnlyList<uint> Hashes,
	IReadOnlyList<string> Names)
{
	public static UnitBoneNames Empty { get; } = new([], []);

	public bool HasValue => Hashes.Count > 0 && Names.Count > 0;
}