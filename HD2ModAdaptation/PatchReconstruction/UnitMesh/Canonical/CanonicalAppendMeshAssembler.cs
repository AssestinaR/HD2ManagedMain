using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Blender object.join equivalent for the geometry portion of a decoration attachment.
// The target remains the active-object shell; both target and source topology survive.
public sealed record CanonicalAppendSectionProvenance(
    int FinalSectionIndex,
    bool IsTargetSection,
    int SourceSectionIndex);

public sealed record CanonicalAppendMeshResult(
    UnitRawMeshData? Mesh,
    IReadOnlyList<CanonicalAppendSectionProvenance> Sections,
    IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
    public bool IsValid => Mesh is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalAppendMeshAssembler
{
    public CanonicalAppendMeshResult TryAppend(
        UnitRawMeshData target,
        UnitRawMeshData source,
        Matrix4x4 sourceToTargetLocal)
        => TryAppendMany(target, [(source, sourceToTargetLocal)]);

    // Build the final LOD geometry in one pass. Repeated TryAppend calls copy the
    // already-merged mesh for every source and become expensive for large attachments.
    public CanonicalAppendMeshResult TryAppendMany(
        UnitRawMeshData target,
        IReadOnlyList<(UnitRawMeshData Source, Matrix4x4 SourceToTargetLocal)> sources)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sources);
        var diagnostics = new List<CanonicalPlanDiagnostic>();
        Validate(target, "Target", diagnostics);
        foreach (var (source, transform) in sources)
        {
            Validate(source, "Source", diagnostics);
            if (target.LodIndex != source.LodIndex)
                diagnostics.Add(new("AppendLodMismatch", "Decoration geometry can only be appended to a target mesh of the same LOD."));
            if (!IsFinite(transform))
                diagnostics.Add(new("InvalidAppendTransform", "The decoration source-to-target transform is not finite."));
        }
        if (diagnostics.Count != 0) return new(null, [], diagnostics);

        var vertexCapacity = checked(target.Vertices.Count + sources.Sum(item => item.Source.Vertices.Count));
        var sectionCapacity = checked(target.Sections.Count + sources.Sum(item => item.Source.Sections.Count));
        var vertices = new List<UnitRawVertexRecord>(vertexCapacity);
        var sections = new List<UnitRawMeshSectionData>(sectionCapacity);
        var provenance = new List<CanonicalAppendSectionProvenance>(sectionCapacity);
        foreach (var (section, index) in target.Sections.Select((value, index) => (value, index)))
        {
            sections.Add(section);
            provenance.Add(new(sections.Count - 1, true, index));
        }
        foreach (var (vertex, index) in target.Vertices.Select((value, index) => (value, index)))
            vertices.Add(vertex with { Index = checked((uint)index) });

        foreach (var (source, sourceToTargetLocal) in sources)
        {
            var identity = sourceToTargetLocal == Matrix4x4.Identity;
            var normalTransform = Matrix4x4.Identity;
            if (!identity)
            {
                if (!Matrix4x4.Invert(sourceToTargetLocal, out var inverse))
                    return new(null, [], [new("NonInvertibleAppendTransform", "The decoration source-to-target transform is not invertible.")]);
                normalTransform = Matrix4x4.Transpose(inverse);
            }
            var offset = checked((uint)vertices.Count);
            foreach (var vertex in source.Vertices)
                vertices.Add(TransformVertex(vertex, sourceToTargetLocal, normalTransform, identity) with { Index = checked((uint)vertices.Count) });
            foreach (var (section, index) in source.Sections.Select((value, index) => (value, index)))
            {
                var triangles = section.Triangles.Select(triangle => new UnitTriangleIndices(
                    checked(triangle.A + offset), checked(triangle.B + offset), checked(triangle.C + offset))).ToArray();
                sections.Add(section with { Triangles = triangles });
                provenance.Add(new(sections.Count - 1, false, index));
            }
        }
        var merged = target with
        {
            Sections = sections,
            Triangles = sections.SelectMany(section => section.Triangles).ToArray(),
            Vertices = vertices
        };
        return new(merged, provenance, []);
    }

    private static void Validate(UnitRawMeshData mesh, string role, List<CanonicalPlanDiagnostic> diagnostics)
    {
        foreach (var section in mesh.Sections)
        foreach (var triangle in section.Triangles)
            if (triangle.A >= mesh.Vertices.Count || triangle.B >= mesh.Vertices.Count || triangle.C >= mesh.Vertices.Count)
                diagnostics.Add(new("AppendIndexOutOfRange", $"{role} decoration section references a vertex outside its mesh."));
    }

    private static UnitRawVertexRecord TransformVertex(UnitRawVertexRecord vertex, Matrix4x4 position, Matrix4x4 normal, bool identity)
    {
        var components = vertex.Components.Select(component =>
        {
            if (identity || component.FloatValues.Length < 3 || component.Type > 3) return component with { RawData = Array.Empty<byte>() };
            var value = new Vector3(component.FloatValues[0], component.FloatValues[1], component.FloatValues[2]);
            value = component.Type == 0 ? Vector3.Transform(value, position) : Vector3.TransformNormal(value, normal);
            var floats = component.FloatValues.ToArray();
            floats[0] = value.X; floats[1] = value.Y; floats[2] = value.Z;
            return component with { FloatValues = floats, RawData = Array.Empty<byte>() };
        }).ToArray();
        return vertex with { Data = Array.Empty<byte>(), Components = components };
    }

    private static bool IsFinite(Matrix4x4 matrix)
        => typeof(Matrix4x4).GetFields().Where(field => field.FieldType == typeof(float))
            .Select(field => (float)field.GetValue(matrix)!).All(float.IsFinite);
}
