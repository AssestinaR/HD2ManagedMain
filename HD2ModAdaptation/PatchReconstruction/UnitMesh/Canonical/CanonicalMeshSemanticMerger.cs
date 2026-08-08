using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Performs the first-version canonical geometry replacement without touching legacy transfer or Manager layers.
// SDK reference entry points: ImportStingrayUnitOperator, object.join(), PrepareMesh(), and GetMeshData().
public sealed record CanonicalMeshSemanticMergeResult(
	UnitRawMeshData? Mesh,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Mesh is not null && Diagnostics.Count == 0;
}

public sealed record CanonicalMeshSemanticMergeRequest(
	CanonicalMeshKey Source,
	CanonicalMeshKey Target,
	Matrix4x4 SourceToTargetLocal,
	CanonicalSectionMappingMode SectionMapping = CanonicalSectionMappingMode.ByOrdinal);

public enum CanonicalSectionMappingMode
{
	// First-version contract: source and target sections correspond by the same ordinal only.
	ByOrdinal = 0
}

public sealed record CanonicalUnmatchedTargetMeshRequest(CanonicalMeshKey Target);

public sealed class CanonicalMeshSemanticMerger
{
	public CanonicalMeshSemanticMergeResult TryMerge(
		CanonicalMeshSemanticMergeRequest request,
		UnitRawMeshData target,
		UnitRawMeshData source)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!request.Source.IsValid || !request.Target.IsValid)
		{
			return new(null, [new("InvalidMeshMapping", "Canonical mesh merge requires explicit valid source and target keys.")]);
		}
		if (request.SectionMapping != CanonicalSectionMappingMode.ByOrdinal)
		{
			return new(null, [new("UnsupportedSectionMapping", "Canonical mesh merge requires explicit same-ordinal section mapping; modulo and automatic guessing are not allowed.")]);
		}

		return TryMerge(target, source, request.SourceToTargetLocal);
	}

	public CanonicalMeshSemanticMergeResult TryPrepareUnmatchedTarget(CanonicalUnmatchedTargetMeshRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return new(null, [new("UnmatchedTargetTinyNotImplemented", $"Target {request.Target} has no source match; tiny mesh construction is not implemented in this canonical step.")]);
	}

	public CanonicalMeshSemanticMergeResult TryMerge(
		UnitRawMeshData target,
		UnitRawMeshData source,
		Matrix4x4 sourceToTargetLocal)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(source);

		var diagnostics = new List<CanonicalPlanDiagnostic>();
		// The target is Blender's active-object shell: its legacy topology is replaced
		// before GetMeshData runs. Retain only its section/material identity here; an
		// unreadable old target triangle must not invalidate the new source topology.
		ValidateMesh(source, "Source", diagnostics);

		if (source.Vertices.Count == 0 || source.Sections.Count == 0 || source.Sections.All(section => section.Triangles.Count == 0))
		{
			diagnostics.Add(new("EmptySourceMesh", "Canonical replacement requires non-empty source vertices, sections, and triangles."));
		}

		var sectionLayout = CanonicalSectionLayout.TryCreate(source, target);
		diagnostics.AddRange(sectionLayout.Diagnostics);

		if (!IsFinite(sourceToTargetLocal))
		{
			diagnostics.Add(new("InvalidTransform", "The source-to-target local transform must contain only finite values."));
		}

		if (diagnostics.Count != 0)
		{
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));
		}

		var isIdentity = sourceToTargetLocal == Matrix4x4.Identity;
		var normalTransform = Matrix4x4.Identity;
		if (!isIdentity)
		{
			if (!Matrix4x4.Invert(sourceToTargetLocal, out var inverse))
			{
				return new(null, [new("NonInvertibleTransform", "A non-identity source-to-target transform must be invertible for normal/tangent semantics.")]);
			}

			normalTransform = Matrix4x4.Transpose(inverse);
		}

		var vertices = source.Vertices
			.Select(vertex => TransformVertex(vertex, sourceToTargetLocal, normalTransform, isIdentity))
			.Select((vertex, index) => vertex with { Index = checked((uint)index) })
			.ToArray();
		// Blender object.join assigns final polygon material slots before SDK GetMeshData.
		// The target's original Section count is shell metadata; joined source material
		// groups become the final sections and are rebuilt by Entry.Save.
		var sections = sectionLayout.OutputSections;

		var merged = target with
		{
			Sections = sections,
			Triangles = sections.SelectMany(section => section.Triangles).ToArray(),
			Vertices = vertices
		};
		return new(merged, Array.Empty<CanonicalPlanDiagnostic>());
	}

	private static void ValidateMesh(UnitRawMeshData mesh, string role, List<CanonicalPlanDiagnostic> diagnostics)
	{
		foreach (var (section, sectionIndex) in mesh.Sections.Select((value, index) => (value, index)))
		{
			foreach (var triangle in section.Triangles)
			{
				if (triangle.A >= mesh.Vertices.Count || triangle.B >= mesh.Vertices.Count || triangle.C >= mesh.Vertices.Count)
				{
					diagnostics.Add(new("IndexOutOfRange", $"{role} section {sectionIndex} references a vertex outside the mesh."));
				}
			}
		}
	}

	private static UnitRawVertexRecord TransformVertex(UnitRawVertexRecord vertex, Matrix4x4 positionTransform, Matrix4x4 normalTransform, bool isIdentity)
	{
		var components = vertex.Components.Select(component =>
		{
			if (isIdentity || component.FloatValues.Length < 3 || component.Type > 3)
			{
				return component;
			}

			var value = new Vector3(component.FloatValues[0], component.FloatValues[1], component.FloatValues[2]);
			value = component.Type == 0
				? Vector3.Transform(value, positionTransform)
				: Vector3.TransformNormal(value, normalTransform);
			var floats = component.FloatValues.ToArray();
			floats[0] = value.X;
			floats[1] = value.Y;
			floats[2] = value.Z;
			return component with { FloatValues = floats, RawData = Array.Empty<byte>() };
		}).ToArray();

		// Blender object.join works on typed mesh attributes, then GetMeshData serializes a new
		// target stream. Never retain source GPU ABI bytes: they can encode a different stride.
		return new UnitRawVertexRecord(vertex.Index, Array.Empty<byte>(), components);
	}

	private static bool IsFinite(Matrix4x4 matrix)
		=> typeof(Matrix4x4).GetFields()
			.Where(field => field.FieldType == typeof(float))
			.Select(field => (float)field.GetValue(matrix)!)
			.All(float.IsFinite);
}
