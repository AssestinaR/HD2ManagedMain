using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModCore.Domain;

// 作用：保存 Unit 结构和几何事实的轻量投影，供计划器和 UI 使用而不保留完整 Payload。
// Purpose: Lightweight Unit structure/geometry projection for planners and UI without retaining full payloads.
public sealed record ModUnitStructureSummary(
	AssetKey UnitAssetKey,
	uint Version,
	ulong BonesReference,
	ulong CompositeReference,
	int MeshCount,
	int VisibleMeshCount,
	int RenderableMeshCount,
	UnitGeometryFacts Geometry,
	IReadOnlyList<ModUnitMeshSummary> Meshes,
	bool IsBoneResolvedFromPatch,
	bool IsCompositeResolvedFromPatch,
	long EstimatedPayloadBytes)
{
	public bool HasRenderableGeometry => Geometry.HasRenderableVisibleGeometry;
	public bool HasUnresolvedDependencies
		=> CompositeReference != 0 && !IsCompositeResolvedFromPatch
			|| BonesReference != 0 && !IsBoneResolvedFromPatch;

	public static ModUnitStructureSummary Create(PatchUnitMesh unit)
	{
		ArgumentNullException.ThrowIfNull(unit);
		var model = unit.Model;
		var geometry = UnitGeometryFactsBuilder.Analyze(model);
		var meshes = model.Meshes.Select(mesh =>
		{
			var fact = geometry.FindMesh(mesh.Index);
			var raw = model.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == mesh.Index);
			return new ModUnitMeshSummary(
				mesh.Index,
				mesh.MeshId,
				mesh.LodIndex,
				mesh.SemanticInfo.IsVisualMesh,
				mesh.SemanticInfo.IsCullingBody,
				fact?.VertexCount ?? 0,
				fact?.TriangleCount ?? 0,
				fact?.Quality ?? UnitMeshGeometryQuality.Unreadable);
		}).ToArray();
		var payloadBytes = (long)unit.Payload.TocData.LongLength
			+ unit.Payload.StreamData.LongLength
			+ unit.Payload.GpuResourceData.LongLength
			+ (unit.CompositePayload is null ? 0L : unit.CompositePayload.TocData.LongLength + unit.CompositePayload.StreamData.LongLength + unit.CompositePayload.GpuResourceData.LongLength);
		return new ModUnitStructureSummary(
			new AssetKey(unit.Entry.AssetKey.TypeId, unit.Entry.AssetKey.FileId),
			model.Version,
			model.BonesRef,
			model.CompositeRef,
			model.Meshes.Count,
			geometry.VisibleMeshes.Count,
			geometry.VisibleMeshes.Count(mesh => mesh.HasRenderableGeometry),
			geometry,
			meshes,
			unit.Dependencies?.IsBoneResolvedFromPatch ?? false,
			unit.Dependencies?.IsCompositeResolvedFromPatch ?? false,
			payloadBytes);
	}
}

// 作用：描述一个 Mesh 的可见性、LOD 和真实几何证据。
// Purpose: Describes visibility, LOD, and renderable geometry evidence for one Mesh.
public sealed record ModUnitMeshSummary(
	int MeshInfoIndex,
	uint MeshId,
	int LodIndex,
	bool IsVisualMesh,
	bool IsCullingMesh,
	int VertexCount,
	int TriangleCount,
	UnitMeshGeometryQuality GeometryQuality);
