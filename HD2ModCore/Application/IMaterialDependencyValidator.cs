using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 source material 到 texture 依赖闭合性验证 API，避免传播缺失资源的材质。
// Purpose: Defines validation APIs for source material to texture dependency closure before material propagation.
public interface IMaterialDependencyValidator
{
	ValueTask<MaterialDependencyValidationResult> ValidateAsync(
		IReadOnlyCollection<ulong> materialIds,
		IReadOnlyList<PatchTocEntry> patchEntries,
		CancellationToken cancellationToken = default);
}