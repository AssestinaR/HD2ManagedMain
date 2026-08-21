using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModCore.Domain;

// 作用：保存一个 Patch 内来源 Unit 的真实几何、可见性和依赖事实，不保留原始 Payload。
// Purpose: Holds real geometry, visibility, and dependency facts for one source Unit without retaining payloads.
public sealed record ModSourceUnitFacts(
	string SourcePatchPath,
	AssetKey UnitAssetKey,
	uint Version,
	ulong BonesReference,
	ulong CompositeReference,
	bool IsBoneResolvedFromPatch,
	bool IsCompositeResolvedFromPatch,
	bool IsHidden,
	string VisibilityReason,
	IReadOnlyList<ModSourceUnitMeshFact> Meshes,
	string? ReadError = null)
{
	public bool IsReadable => string.IsNullOrWhiteSpace(ReadError);
	public bool HasUnresolvedExternalBone => BonesReference != 0 && !IsBoneResolvedFromPatch;
	public bool HasUnresolvedExternalComposite => CompositeReference != 0 && !IsCompositeResolvedFromPatch;
	public bool HasRenderableVisibleGeometry => Meshes.Any(mesh => mesh.IsVisualMesh && mesh.HasRenderableGeometry);
	public bool HasTransferableGeometry => IsReadable && !IsHidden && Meshes.Any(mesh => mesh.IsTransferable);
}

// 作用：保存来源 Unit 中单个 Mesh 的真实几何和语义证据，供领域策略决定能否使用。
// Purpose: Holds real geometry and semantic evidence for one source mesh; domain policy decides eligibility.
public sealed record ModSourceUnitMeshFact(
	int MeshInfoIndex,
	uint MeshId,
	int LodIndex,
	bool IsVisualMesh,
	bool IsCullingMesh,
	UnitMeshGeometryQuality GeometryQuality,
	int VertexCount,
	int TriangleCount,
	string SemanticName,
	string Slot,
	string PieceType,
	string BodyType,
	string Weight)
{
	public bool HasRenderableGeometry => GeometryQuality is UnitMeshGeometryQuality.Renderable or UnitMeshGeometryQuality.RenderableLod0;
	public bool IsTransferable => HasRenderableGeometry && MeshId != 0;
}

// 作用：保存一次 Patch 来源 Unit 扫描的事实集合和逐项读取失败，不把任一失败误解为“没有模型”。
// Purpose: Holds one Patch's source-unit facts and per-unit failures without treating a failure as no model.
public sealed record ModSourceUnitFactsSnapshot(
	string SourcePatchPath,
	IReadOnlyList<ModSourceUnitFacts> Units)
{
	public IReadOnlyList<ModSourceUnitFacts> ReadableUnits => Units.Where(unit => unit.IsReadable).ToArray();
	public IReadOnlyList<ModSourceUnitFacts> TransferableUnits => Units.Where(unit => unit.HasTransferableGeometry).ToArray();
	public bool HasReadFailures => Units.Any(unit => !unit.IsReadable);
}
