using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Builds SDK/autofix-compatible tiny RawMesh placeholders while preserving target mesh identity and material sections.
public sealed record CanonicalPlaceholderMinificationResult(
	UnitRawMeshData? Mesh,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Mesh is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalPlaceholderMinifier
{
	private const float TinySize = 0.0001f;

	public CanonicalPlaceholderMinificationResult TryMinify(UnitRawMeshData target, UnitStreamInfo targetStream)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(targetStream);

		if (target.Sections.Count == 0)
			return new(null, [new("TargetSectionsMissing", $"Target RawMesh {target.MeshInfoIndex} has no material sections.")]);
		if (targetStream.VertexStride == 0 || targetStream.Components.Count == 0)
			return new(null, [new("InvalidTargetStream", $"Target RawMesh {target.MeshInfoIndex} has no encodable stream ABI.")]);
		var materialLayout = CanonicalFinalMaterialLayout.TryCreate(target);
		if (!materialLayout.IsValid)
			return new(null, materialLayout.Diagnostics);

		var vertices = new List<UnitRawVertexRecord>(3);
		var positions = new[]
		{
			new Vector3(0, 0, 0),
			new Vector3(TinySize, 0, 0),
			new Vector3(0, TinySize, 0)
		};
		for (var index = 0; index < positions.Length; index++)
		{
			var components = targetStream.Components.Select(component => CreateComponent(component, positions[index])).ToArray();
			vertices.Add(new UnitRawVertexRecord((uint)index, Array.Empty<byte>(), components));
		}

		var sections = target.Sections.Select((section, index) => new UnitRawMeshSectionData(
			materialLayout.GetMaterialOrdinal(index),
			section.MaterialSlotId,
			index == 0 ? [new UnitTriangleIndices(0, 1, 2)] : Array.Empty<UnitTriangleIndices>())).ToArray();
		return new(target with
		{
			Sections = sections,
			Triangles = sections.SelectMany(section => section.Triangles).ToArray(),
			Vertices = vertices
		}, Array.Empty<CanonicalPlanDiagnostic>());
	}

	private static UnitVertexComponentValue CreateComponent(UnitStreamComponentInfo component, Vector3 position)
	{
		var values = component.Type switch
		{
			0 => component.FormatName == "float" ? [position.X] : [position.X, position.Y, position.Z, 0f],
			1 or 3 => [0f, 0f, 1f, 0f],
			2 => [1f, 0f, 0f, 0f],
			4 => [0f, 0f, 0f, 0f],
			5 => [1f, 1f, 1f, 1f],
			7 => [1f, 0f, 0f, 0f],
			_ => Array.Empty<float>()
		};
		var uintValues = component.Type == 6 ? [0u, 0u, 0u, 0u] : Array.Empty<uint>();
		return new(component.Type, component.TypeName, component.Format, component.FormatName, component.Index, values, uintValues, Array.Empty<byte>());
	}
}