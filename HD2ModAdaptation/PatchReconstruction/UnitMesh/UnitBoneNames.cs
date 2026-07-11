namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// 浣滅敤锛氫繚瀛?Unit Bones 璧勬簮涓殑楠ㄩ hash 涓庡悕绉帮紝渚?SDK 椋庢牸 mesh 鍛藉悕浣跨敤銆?
// Purpose: Holds bone hashes and names from a Unit Bones resource for SDK-style mesh naming.
public sealed record UnitBoneNames(
	IReadOnlyList<uint> Hashes,
	IReadOnlyList<string> Names)
{
	public static UnitBoneNames Empty { get; } = new([], []);

	public bool HasValue => Hashes.Count > 0 && Names.Count > 0;
}