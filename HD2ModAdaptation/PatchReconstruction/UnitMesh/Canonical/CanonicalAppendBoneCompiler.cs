using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Rebuilds one target LOD palette after Blender-style topology append. A vertex is copied per
// final material section when necessary because Stingray Type=6 values are section-remap local.
public sealed record CanonicalAppendBoneResult(UnitRawMeshData? Mesh, UnitBoneInfo? BoneInfo, IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
    public bool IsValid => Mesh is not null && BoneInfo is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalAppendBoneCompiler
{
    public CanonicalAppendBoneResult TryCompile(
        UnitMeshModel targetModel, UnitRawMeshData targetRaw,
        UnitMeshModel decorationModel, UnitRawMeshData decorationRaw,
        UnitRawMeshData appended, IReadOnlyList<CanonicalAppendSectionProvenance> provenance)
    {
        var errors = new List<CanonicalPlanDiagnostic>();
        if (targetRaw.LodIndex < 0 || targetRaw.LodIndex >= targetModel.BoneInfos.Count)
            errors.Add(new("AppendTargetBoneInfoMissing", "The target decoration LOD has no writable BoneInfo."));
        if (decorationRaw.LodIndex < 0 || decorationRaw.LodIndex >= decorationModel.BoneInfos.Count)
            errors.Add(new("AppendDecorationBoneInfoMissing", "The decoration LOD has no readable BoneInfo."));
        if (appended.Sections.Count != provenance.Count)
            errors.Add(new("AppendSectionProvenanceMismatch", "The appended mesh section provenance does not match final sections."));
        var layout = CanonicalFinalMaterialLayout.TryCreate(appended);
        errors.AddRange(layout.Diagnostics);
        if (errors.Count != 0) return new(null, null, errors);

        var hashesBySection = new List<HashSet<uint>>(appended.Sections.Count);
        for (var index = 0; index < appended.Sections.Count; index++)
        {
            var origin = provenance[index];
            var model = origin.IsTargetSection ? targetModel : decorationModel;
            var raw = origin.IsTargetSection ? targetRaw : decorationRaw;
            var sourceSection = raw.Sections[origin.SourceSectionIndex];
            hashesBySection.Add(ResolveSectionHashes(model, raw, sourceSection, errors));
        }
        if (errors.Count != 0) return new(null, null, errors);
        var hashesByMaterial = new Dictionary<uint, HashSet<uint>>();
        for (var index = 0; index < hashesBySection.Count; index++)
        {
            var material = layout.GetMaterialOrdinal(index);
            if (!hashesByMaterial.TryGetValue(material, out var hashes)) hashesByMaterial[material] = hashes = [];
            hashes.UnionWith(hashesBySection[index]);
        }
        var realIndices = hashesByMaterial.Values.SelectMany(value => value).Distinct().OrderBy(value => value)
            .Select(hash => IndexOf(targetModel.TransformNameHashes, hash)).ToArray();
        if (realIndices.Any(index => index < 0))
        {
            errors.Add(new("AppendTargetBoneMissing", "A decoration bone is absent from the target TransformInfo."));
            return new(null, null, errors);
        }
        var remaps = new List<UnitBoneRemap>();
        var remapOffset = checked((uint)(4 + hashesByMaterial.Count * 8));
        foreach (var (material, hashes) in hashesByMaterial.OrderBy(item => item.Key))
        {
            var fake = hashes.OrderBy(hash => hash).Select(hash => checked((uint)Array.IndexOf(realIndices, IndexOf(targetModel.TransformNameHashes, hash)))).ToArray();
            remaps.Add(new UnitBoneRemap(checked((int)material), remapOffset, fake));
            remapOffset += checked((uint)(fake.Length * sizeof(uint)));
        }

        var targetCount = targetRaw.Vertices.Count;
        var vertices = new List<UnitRawVertexRecord>();
        var sections = new List<UnitRawMeshSectionData>();
        for (var finalIndex = 0; finalIndex < appended.Sections.Count; finalIndex++)
        {
            var origin = provenance[finalIndex];
            var raw = origin.IsTargetSection ? targetRaw : decorationRaw;
            var model = origin.IsTargetSection ? targetModel : decorationModel;
            var original = raw.Sections[origin.SourceSectionIndex];
            var finalSection = appended.Sections[finalIndex];
            var remap = remaps.Single(item => item.MaterialIndex == finalSection.MaterialIndex);
            var map = new Dictionary<uint, uint>();
            uint Encode(uint sourceIndex)
            {
                if (map.TryGetValue(sourceIndex, out var existing)) return existing;
                if (sourceIndex >= raw.Vertices.Count) { errors.Add(new("AppendBoneIndexOutOfRange", "A decoration triangle references a vertex outside its source mesh.")); return 0; }
                var mergedIndex = origin.IsTargetSection ? sourceIndex : checked((uint)(targetCount + sourceIndex));
                var vertex = appended.Vertices[(int)mergedIndex];
                var rewritten = vertex with { Index = checked((uint)vertices.Count), Data = Array.Empty<byte>(), Components = RewriteIndices(vertex.Components, model, raw, original.MaterialIndex, targetModel.TransformNameHashes, realIndices, remap, errors) };
                map.Add(sourceIndex, rewritten.Index); vertices.Add(rewritten); return rewritten.Index;
            }
            var triangles = original.Triangles.Select(triangle => new UnitTriangleIndices(Encode(triangle.A), Encode(triangle.B), Encode(triangle.C))).ToArray();
            sections.Add(finalSection with { Triangles = triangles });
        }
        if (errors.Count != 0) return new(null, null, errors);
        var mesh = appended with { Vertices = vertices, Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray() };
        var targetMesh = targetModel.Meshes.SingleOrDefault(mesh => mesh.Index == targetRaw.MeshInfoIndex);
        if (targetMesh is null)
        {
            errors.Add(new("AppendTargetMeshMissing", "The target decoration mesh is absent from its Unit model."));
            return new(null, null, errors);
        }
        var matrices = CanonicalInverseJointMatrixCompiler.Build(targetModel, targetMesh.TransformIndex, realIndices, errors);
        if (errors.Count != 0) return new(null, null, errors);
        var template = targetModel.BoneInfos[targetRaw.LodIndex];
        var palette = template with { NumBones = checked((uint)realIndices.Length), RealIndices = realIndices.Select(index => checked((uint)index)).ToArray(), Remaps = remaps, BoneMatrices = matrices };
        return new(mesh, palette, []);
    }

    private static HashSet<uint> ResolveSectionHashes(UnitMeshModel model, UnitRawMeshData raw, UnitRawMeshSectionData section, List<CanonicalPlanDiagnostic> errors)
    {
        var result = new HashSet<uint>();
        var info = model.BoneInfos[raw.LodIndex];
        var remap = info.Remaps.FirstOrDefault(item => item.MaterialIndex == section.MaterialIndex) ?? info.Remaps.FirstOrDefault();
        if (remap is null) { errors.Add(new("AppendSourceBoneRemapMissing", "A source mesh section has no BoneInfo remap.")); return result; }
        foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
        {
            foreach (var fake in raw.Vertices[(int)vertexIndex].Components.FirstOrDefault(component => component.Type == 6)?.UIntValues ?? [])
            {
                if (fake >= remap.FakeIndices.Count || remap.FakeIndices[(int)fake] >= info.RealIndices.Count) { errors.Add(new("AppendSourceBoneIndexInvalid", "A source Type=6 index cannot be resolved.")); continue; }
                var transform = info.RealIndices[(int)remap.FakeIndices[(int)fake]];
                if (transform >= model.TransformNameHashes.Count) { errors.Add(new("AppendSourceBoneMissing", "A source BoneInfo transform index is missing.")); continue; }
                result.Add(model.TransformNameHashes[(int)transform]);
            }
        }
        return result;
    }

    private static IReadOnlyList<UnitVertexComponentValue> RewriteIndices(IReadOnlyList<UnitVertexComponentValue> components, UnitMeshModel model, UnitRawMeshData raw, uint sourceMaterial, IReadOnlyList<uint> targetHashes, IReadOnlyList<int> finalReal, UnitBoneRemap finalRemap, List<CanonicalPlanDiagnostic> errors)
    {
        var info = model.BoneInfos[raw.LodIndex];
        var sourceRemap = info.Remaps.FirstOrDefault(item => item.MaterialIndex == sourceMaterial) ?? info.Remaps.FirstOrDefault();
        return components.Select(component => component.Type != 6 ? component : component with
        {
            RawData = Array.Empty<byte>(),
            UIntValues = component.UIntValues.Select(fake => Resolve(fake)).ToArray()
        }).ToArray();
        uint Resolve(uint fake)
        {
            if (sourceRemap is null || fake >= sourceRemap.FakeIndices.Count || sourceRemap.FakeIndices[(int)fake] >= info.RealIndices.Count) { errors.Add(new("AppendSourceBoneIndexInvalid", "A source vertex has an invalid Type=6 index.")); return 0; }
            var transform = info.RealIndices[(int)sourceRemap.FakeIndices[(int)fake]];
            if (transform >= model.TransformNameHashes.Count) { errors.Add(new("AppendSourceBoneMissing", "A source vertex references an unavailable transform.")); return 0; }
            var real = Array.IndexOf(finalReal.ToArray(), IndexOf(targetHashes, model.TransformNameHashes[(int)transform]));
            var result = Array.IndexOf(finalRemap.FakeIndices.ToArray(), checked((uint)real));
            if (real < 0 || result < 0) { errors.Add(new("AppendTargetBoneRemapMissing", "A final target palette does not contain a required source bone.")); return 0; }
            return checked((uint)result);
        }
    }

    private static int IndexOf(IReadOnlyList<uint> values, uint value) { for (var i = 0; i < values.Count; i++) if (values[i] == value) return i; return -1; }
}
