namespace HD2ModCore.Domain;

// 作用：描述 source material 及其 texture 依赖是否能在 patch 输出中闭合。
// Purpose: Describes whether a source material and its texture dependencies are closed in patch output.
public sealed record MaterialDependencyValidationResult(
	IReadOnlySet<ulong> ValidMaterialIds,
	IReadOnlyDictionary<ulong, IReadOnlyList<ulong>> MaterialTextureIds,
	IReadOnlyDictionary<ulong, string> RejectedMaterialReasons)
{
	public bool IsValid(ulong materialId) => ValidMaterialIds.Contains(materialId);
}