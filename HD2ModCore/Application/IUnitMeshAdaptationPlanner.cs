using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 source mod Unit mesh 到原版 archive target Unit 模板的自动适配 dry-run 规划 API。
// Purpose: Defines dry-run planning APIs for adapting source mod Unit meshes onto vanilla archive target Unit templates.
public interface IUnitMeshAdaptationPlanner
{
	UnitMeshAdaptationPlan BuildPlan(
		PatchUnitMesh sourceUnit,
		ArchiveUnitMesh targetTemplate,
		int? sourceMeshInfoIndex = null);
}
