namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// A final section keeps its original skinning owner. SourceIndex == -1 identifies target-shell geometry.
public sealed record CanonicalAppendSource(UnitMeshModel Model, UnitRawMeshData RawMesh);
public sealed record CanonicalAppendSectionOrigin(int FinalSectionIndex, int SourceIndex, int SourceSectionIndex);

public sealed record CanonicalAppendBoneResult(UnitRawMeshData? Mesh, UnitBoneInfo? BoneInfo, IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
    public bool IsValid => Mesh is not null && BoneInfo is not null && Diagnostics.Count == 0;
}

// Rebuilds one shared target LOD palette after Blender-style topology append. Vertices are copied
// per final section because Stingray Type=6 values are section-remap local.
public sealed class CanonicalAppendBoneCompiler
{
    public CanonicalAppendBoneResult TryCompile(
        UnitMeshModel targetModel, UnitRawMeshData targetRaw,
        IReadOnlyList<CanonicalAppendSource> sources,
        UnitRawMeshData appended, IReadOnlyList<CanonicalAppendSectionOrigin> origins)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var errors = new List<CanonicalPlanDiagnostic>();
        if (targetRaw.LodIndex < 0 || targetRaw.LodIndex >= targetModel.BoneInfos.Count)
            errors.Add(new("AppendTargetBoneInfoMissing", "The target decoration LOD has no writable BoneInfo."));
        if (sources.Count == 0) errors.Add(new("AppendSourcesMissing", "Decoration append requires at least one source mesh."));
        foreach (var source in sources)
            if (source.RawMesh.LodIndex < 0 || source.RawMesh.LodIndex >= source.Model.BoneInfos.Count)
                errors.Add(new("AppendDecorationBoneInfoMissing", "A decoration LOD has no readable BoneInfo."));
        if (appended.Sections.Count != origins.Count)
            errors.Add(new("AppendSectionProvenanceMismatch", "The appended mesh section provenance does not match final sections."));
        var layout = CanonicalFinalMaterialLayout.TryCreate(appended);
        errors.AddRange(layout.Diagnostics);
        if (errors.Count != 0) return new(null, null, errors);

        (UnitMeshModel Model, UnitRawMeshData RawMesh, UnitRawMeshSectionData Section) Resolve(CanonicalAppendSectionOrigin origin)
        {
            if (origin.SourceIndex == -1)
                return (targetModel, targetRaw, targetRaw.Sections[origin.SourceSectionIndex]);
            if (origin.SourceIndex < 0 || origin.SourceIndex >= sources.Count)
                throw new InvalidDataException("A final decoration section has an invalid source owner.");
            var source = sources[origin.SourceIndex];
            return (source.Model, source.RawMesh, source.RawMesh.Sections[origin.SourceSectionIndex]);
        }

        var hashesBySection = new List<HashSet<uint>>(appended.Sections.Count);
        try
        {
            foreach (var origin in origins.OrderBy(origin => origin.FinalSectionIndex))
            {
                var source = Resolve(origin);
                hashesBySection.Add(ResolveSectionHashes(source.Model, source.RawMesh, source.Section, errors));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            errors.Add(new("AppendSectionOriginInvalid", exception.Message));
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
        var remaps = BuildRemaps(hashesByMaterial, targetModel.TransformNameHashes, realIndices);

        var vertices = new List<UnitRawVertexRecord>();
        var sections = new List<UnitRawMeshSectionData>();
        for (var finalIndex = 0; finalIndex < appended.Sections.Count; finalIndex++)
        {
            var source = Resolve(origins[finalIndex]);
            var finalSection = appended.Sections[finalIndex];
            var remap = remaps.Single(item => item.MaterialIndex == finalSection.MaterialIndex);
            var map = new Dictionary<uint, uint>();
            uint Encode(uint mergedIndex)
            {
                if (map.TryGetValue(mergedIndex, out var existing)) return existing;
                if (mergedIndex >= appended.Vertices.Count) { errors.Add(new("AppendBoneIndexOutOfRange", "A final decoration triangle references a vertex outside its mesh.")); return 0; }
                var vertex = appended.Vertices[(int)mergedIndex];
                var rewritten = vertex with
                {
                    Index = checked((uint)vertices.Count), Data = Array.Empty<byte>(),
                    Components = RewriteIndices(vertex.Components, source.Model, source.RawMesh, source.Section.MaterialIndex,
                        targetModel.TransformNameHashes, realIndices, remap, errors)
                };
                map.Add(mergedIndex, rewritten.Index); vertices.Add(rewritten); return rewritten.Index;
            }
            var triangles = finalSection.Triangles.Select(triangle => new UnitTriangleIndices(Encode(triangle.A), Encode(triangle.B), Encode(triangle.C))).ToArray();
            sections.Add(finalSection with { Triangles = triangles });
        }
        if (errors.Count != 0) return new(null, null, errors);

        var targetMesh = targetModel.Meshes.SingleOrDefault(mesh => mesh.Index == targetRaw.MeshInfoIndex);
        if (targetMesh is null) return new(null, null, [new("AppendTargetMeshMissing", "The target decoration mesh is absent from its Unit model.")]);
        var matrices = CanonicalInverseJointMatrixCompiler.Build(targetModel, targetMesh.TransformIndex, realIndices, errors);
        if (errors.Count != 0) return new(null, null, errors);
        var template = targetModel.BoneInfos[targetRaw.LodIndex];
        var palette = template with { NumBones = checked((uint)realIndices.Length), RealIndices = realIndices.Select(index => checked((uint)index)).ToArray(), Remaps = remaps, BoneMatrices = matrices };
        var mesh = appended with { Vertices = vertices, Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray() };
        return new(mesh, palette, []);
    }

    private static IReadOnlyList<UnitBoneRemap> BuildRemaps(IReadOnlyDictionary<uint, HashSet<uint>> hashesByMaterial, IReadOnlyList<uint> targetHashes, IReadOnlyList<int> realIndices)
    {
        var remaps = new List<UnitBoneRemap>();
        var offset = checked((uint)(4 + hashesByMaterial.Count * 8));
        foreach (var (material, hashes) in hashesByMaterial.OrderBy(item => item.Key))
        {
            var fake = hashes.OrderBy(hash => hash).Select(hash => checked((uint)Array.IndexOf(realIndices.ToArray(), IndexOf(targetHashes, hash)))).ToArray();
            remaps.Add(new UnitBoneRemap(checked((int)material), offset, fake));
            offset += checked((uint)(fake.Length * sizeof(uint)));
        }
        return remaps;
    }

    private static HashSet<uint> ResolveSectionHashes(UnitMeshModel model, UnitRawMeshData raw, UnitRawMeshSectionData section, List<CanonicalPlanDiagnostic> errors)
    {
        var result = new HashSet<uint>();
        var info = model.BoneInfos[raw.LodIndex];
        var remap = info.Remaps.FirstOrDefault(item => item.MaterialIndex == section.MaterialIndex) ?? info.Remaps.FirstOrDefault();
        if (remap is null) { errors.Add(new("AppendSourceBoneRemapMissing", "A source mesh section has no BoneInfo remap.")); return result; }
        foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
        {
            if (vertexIndex >= raw.Vertices.Count) { errors.Add(new("AppendSourceVertexInvalid", "A source section references a vertex outside its mesh.")); continue; }
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
        return components.Select(component => component.Type != 6 ? component : component with { RawData = Array.Empty<byte>(), UIntValues = component.UIntValues.Select(Resolve).ToArray() }).ToArray();
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
