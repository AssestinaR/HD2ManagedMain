namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Provides one authoritative geometry classification for Unit selection and reconstruction.
public enum UnitMeshGeometryQuality
{
	Unreadable = 0,
	Placeholder = 1,
	CullingOnly = 2,
	Renderable = 3,
	RenderableLod0 = 4
}

public sealed record UnitMeshGeometryFact(
	int MeshInfoIndex,
	int LodIndex,
	int VertexCount,
	int TriangleCount,
	bool IsVisualMesh,
	bool IsCullingMesh,
	UnitMeshGeometryQuality Quality)
{
	public bool IsReadable => Quality != UnitMeshGeometryQuality.Unreadable;
	public bool IsPlaceholder => Quality == UnitMeshGeometryQuality.Placeholder;
	public bool HasRenderableGeometry => Quality is UnitMeshGeometryQuality.Renderable or UnitMeshGeometryQuality.RenderableLod0;
}

public sealed record UnitGeometryFacts(IReadOnlyList<UnitMeshGeometryFact> Meshes)
{
	public IReadOnlyList<UnitMeshGeometryFact> VisibleMeshes => Meshes.Where(mesh => mesh.IsVisualMesh).ToArray();
	public bool HasRenderableVisibleGeometry => VisibleMeshes.Any(mesh => mesh.HasRenderableGeometry);
	public bool IsFullyHidden => VisibleMeshes.Count != 0 && VisibleMeshes.All(mesh => mesh.IsPlaceholder);

	public UnitMeshGeometryFact? FindMesh(int meshInfoIndex)
		=> Meshes.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex);
}

public static class UnitGeometryFactsBuilder
{
	public static UnitGeometryFacts Analyze(UnitMeshModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		var rawByMesh = model.RawMeshData.ToDictionary(raw => raw.MeshInfoIndex);
		return new UnitGeometryFacts(model.Meshes.Select(mesh =>
		{
			if (!rawByMesh.TryGetValue(mesh.Index, out var raw))
				return new UnitMeshGeometryFact(mesh.Index, mesh.LodIndex, 0, 0, mesh.SemanticInfo.IsVisualMesh, mesh.SemanticInfo.IsCullingBody, UnitMeshGeometryQuality.Unreadable);

			var triangles = CountTriangles(raw);
			var renderable = HasRenderableGeometry(raw);
			var quality = renderable
				? raw.LodIndex == 0 && mesh.SemanticInfo.IsVisualMesh ? UnitMeshGeometryQuality.RenderableLod0 : UnitMeshGeometryQuality.Renderable
				: mesh.SemanticInfo.IsCullingBody || raw.LodIndex == -1 ? UnitMeshGeometryQuality.CullingOnly : UnitMeshGeometryQuality.Placeholder;
			return new UnitMeshGeometryFact(mesh.Index, raw.LodIndex, raw.Vertices.Count, triangles, mesh.SemanticInfo.IsVisualMesh, mesh.SemanticInfo.IsCullingBody, quality);
		}).ToArray());
	}

	public static bool HasRenderableGeometry(UnitRawMeshData raw)
		=> raw.Vertices.Count > 3 && CountTriangles(raw) > 1;

	public static int CountTriangles(UnitRawMeshData raw)
		=> raw.Triangles.Count != 0 ? raw.Triangles.Count : raw.Sections.Sum(section => section.Triangles.Count);

	public static UnitRawMeshData? SelectBestRenderableLod0(UnitMeshModel model)
		=> model.RawMeshData
			.Where(raw => raw.LodIndex == 0 && HasRenderableGeometry(raw))
			.OrderByDescending(CountTriangles)
			.ThenByDescending(raw => raw.Vertices.Count)
			.FirstOrDefault();
}

// Purpose: Centralizes the stable preference order used after a workflow has already applied its semantic rules.
public static class UnitGeometryRanker
{
	public static int GetRank(UnitMeshGeometryQuality quality) => (int)quality;
}
