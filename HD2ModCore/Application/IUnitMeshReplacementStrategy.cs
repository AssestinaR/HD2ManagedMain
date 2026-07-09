using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 Unit RawMesh 自动替换候选选择 API，按结构兼容性选择安全 slot。
// Purpose: Defines Unit RawMesh automatic replacement candidate selection APIs based on structural compatibility.
public interface IUnitMeshReplacementStrategy
{
	IReadOnlyList<UnitMeshReplacementCandidate> FindCandidates(UnitMeshModel targetModel, UnitMeshModel sourceModel);
}
