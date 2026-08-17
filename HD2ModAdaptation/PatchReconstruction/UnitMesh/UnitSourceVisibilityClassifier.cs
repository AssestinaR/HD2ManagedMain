namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Distinguishes an intentionally minified source Unit from one containing visible model geometry.
public static class UnitSourceVisibilityClassifier
{
	private const float MaximumPlaceholderExtent = 0.001f;

	public static UnitSourceVisibilityClassification Classify(PatchUnitMesh unit)
	{
		ArgumentNullException.ThrowIfNull(unit);
		var visibleMeshes = unit.Model.Meshes
			.Where(mesh => mesh.LodIndex >= 0 && mesh.SemanticInfo.IsVisualMesh)
			.Select(mesh => new
			{
				Mesh = mesh,
				Raw = unit.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == mesh.Index)
			})
			.ToArray();
		if (visibleMeshes.Length == 0 || visibleMeshes.Any(item => item.Raw is null))
			return new(false, "NoReadableVisibleMeshes");
		if (visibleMeshes.Any(item => item.Raw!.Vertices.Count > 3 || item.Raw.Triangles.Count > 1))
			return new(false, "VisibleMeshHasRealGeometry");
		if (visibleMeshes.Any(item => !HasPlaceholderBounds(item.Raw!)))
			return new(false, "PlaceholderBoundsTooLargeOrUnreadable");
		return new(true, "TinyPlaceholderGeometry");
	}

	private static bool HasPlaceholderBounds(UnitRawMeshData mesh)
	{
		if (mesh.Vertices.Count == 0) return false;
		var positions = mesh.Vertices
			.Select(vertex => vertex.Components.FirstOrDefault(component => component.Type == 0 && component.Index == 0))
			.ToArray();
		if (positions.Any(position => position is null || position.FloatValues.Length < 3)) return false;

		var minX = float.PositiveInfinity;
		var minY = float.PositiveInfinity;
		var minZ = float.PositiveInfinity;
		var maxX = float.NegativeInfinity;
		var maxY = float.NegativeInfinity;
		var maxZ = float.NegativeInfinity;
		foreach (var position in positions)
		{
			var values = position!.FloatValues;
			if (!float.IsFinite(values[0]) || !float.IsFinite(values[1]) || !float.IsFinite(values[2])) return false;
			minX = Math.Min(minX, values[0]); maxX = Math.Max(maxX, values[0]);
			minY = Math.Min(minY, values[1]); maxY = Math.Max(maxY, values[1]);
			minZ = Math.Min(minZ, values[2]); maxZ = Math.Max(maxZ, values[2]);
		}
		return Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) <= MaximumPlaceholderExtent;
	}
}

public sealed record UnitSourceVisibilityClassification(bool IsHidden, string Reason);
