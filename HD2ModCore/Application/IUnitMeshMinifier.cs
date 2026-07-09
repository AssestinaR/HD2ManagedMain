using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 Unit RawMesh 极小化 API，用于把目标 mesh 压缩成占位三角形。
// Purpose: Defines the Unit RawMesh minifier API for shrinking target meshes into placeholder triangles.
public interface IUnitMeshMinifier
{
	UnitMeshModel MinifyAll(UnitMeshModel model);

	UnitMeshModel MinifyRawMesh(UnitMeshModel model, int meshInfoIndex);
}
