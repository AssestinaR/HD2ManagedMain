using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 Unit RawMesh 重定向 API，用 source mesh 替换 target mesh slot。
// Purpose: Defines the Unit RawMesh retargeting API for replacing a target mesh slot with a source mesh.
public interface IUnitMeshRetargeter
{
	UnitMeshModel ReplaceRawMesh(UnitMeshModel targetModel, int targetMeshInfoIndex, UnitMeshModel sourceModel, int sourceMeshInfoIndex);
}
