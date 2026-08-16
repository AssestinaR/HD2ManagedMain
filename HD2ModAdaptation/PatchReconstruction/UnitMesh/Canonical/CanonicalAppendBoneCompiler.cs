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

        // Blender object.join keeps the host object's vertex groups. SetRemap then emits every
        // one of those groups for every final material slot, including groups without a visible
        // influence in this particular mesh. Compressing this to active triangle bones changes
        // the runtime palette ABI and causes animated geometry to detach in game.
        var template = targetModel.BoneInfos[targetRaw.LodIndex];
        var groupHashes = CollectBlenderVertexGroups(targetModel, targetRaw, errors).ToList();
        foreach (var source in sources)
        {
            foreach (var hash in CollectBlenderVertexGroups(source.Model, source.RawMesh, errors))
            {
                if (!groupHashes.Contains(hash)) groupHashes.Add(hash);
            }
        }
        if (errors.Count != 0) return new(null, null, errors);

        var realIndices = template.RealIndices.Select(index => checked((int)index)).ToList();
        foreach (var hash in groupHashes)
        {
            var transformIndex = IndexOf(targetModel.TransformNameHashes, hash);
            if (transformIndex < 0)
            {
                errors.Add(new("AppendTargetBoneMissing", "A decoration bone is absent from the target TransformInfo."));
                continue;
            }
            if (!realIndices.Contains(transformIndex)) realIndices.Add(transformIndex);
        }
        if (errors.Count != 0) return new(null, null, errors);
        var remaps = BuildRemaps(layout, groupHashes, targetModel.TransformNameHashes, realIndices, errors);
        if (errors.Count != 0) return new(null, null, errors);

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
        var palette = template with { NumBones = checked((uint)realIndices.Count), RealIndices = realIndices.Select(index => checked((uint)index)).ToArray(), Remaps = remaps, BoneMatrices = matrices };
        var mesh = appended with { Vertices = vertices, Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray() };
        return new(mesh, palette, []);
    }

    private static IReadOnlyList<UnitBoneRemap> BuildRemaps(
        CanonicalFinalMaterialLayoutResult layout,
        IReadOnlyList<uint> groupHashes,
        IReadOnlyList<uint> targetHashes,
        IReadOnlyList<int> realIndices,
        List<CanonicalPlanDiagnostic> errors)
    {
        var remaps = new List<UnitBoneRemap>();
        var offset = checked((uint)(4 + layout.Slots.Count * 8));
        var fake = new List<uint>(groupHashes.Count);
        foreach (var hash in groupHashes)
        {
            var transformIndex = IndexOf(targetHashes, hash);
            var paletteIndex = IndexOf(realIndices, transformIndex);
            if (transformIndex < 0 || paletteIndex < 0)
            {
                errors.Add(new("AppendTargetBoneRemapMissing", "A Blender vertex group is absent from the final target palette."));
                continue;
            }
            fake.Add(checked((uint)paletteIndex));
        }
        foreach (var slot in layout.Slots)
        {
            remaps.Add(new UnitBoneRemap(checked((int)slot.MaterialOrdinal), offset, fake.ToArray()));
            offset += checked((uint)(fake.Count * sizeof(uint)));
        }
        return remaps;
    }

    private static IReadOnlyList<uint> CollectBlenderVertexGroups(UnitMeshModel model, UnitRawMeshData raw, List<CanonicalPlanDiagnostic> errors)
    {
        if (raw.LodIndex < 0 || raw.LodIndex >= model.BoneInfos.Count)
        {
            errors.Add(new("AppendSourceBoneInfoMissing", "A Blender vertex-group source has no readable BoneInfo."));
            return [];
        }
        var result = new List<uint>();
        var info = model.BoneInfos[raw.LodIndex];
        var materialByVertex = new uint[raw.Vertices.Count];
        foreach (var section in raw.Sections)
        {
            foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }))
            {
                if (vertexIndex < materialByVertex.Length) materialByVertex[vertexIndex] = section.MaterialIndex;
            }
        }
        for (var vertexIndex = 0; vertexIndex < raw.Vertices.Count; vertexIndex++)
        {
            var remap = info.Remaps.FirstOrDefault(item => item.MaterialIndex == materialByVertex[vertexIndex]) ?? info.Remaps.FirstOrDefault();
            if (remap is null) { errors.Add(new("AppendSourceBoneRemapMissing", "A source mesh section has no BoneInfo remap.")); continue; }
            foreach (var fake in raw.Vertices[vertexIndex].Components.FirstOrDefault(component => component.Type == 6)?.UIntValues ?? [])
            {
                if (fake >= remap.FakeIndices.Count || remap.FakeIndices[(int)fake] >= info.RealIndices.Count) { errors.Add(new("AppendSourceBoneIndexInvalid", "A source Type=6 index cannot be resolved.")); continue; }
                var transform = info.RealIndices[(int)remap.FakeIndices[(int)fake]];
                if (transform >= model.TransformNameHashes.Count) { errors.Add(new("AppendSourceBoneMissing", "A source BoneInfo transform index is missing.")); continue; }
                var hash = model.TransformNameHashes[(int)transform];
                if (!result.Contains(hash)) result.Add(hash);
            }
        }
        // CreateModel appends every palette bone that was not encountered while importing
        // weighted vertices. Preserve this final step exactly.
        for (var transform = 0; transform < model.TransformNameHashes.Count; transform++)
        {
            if (!info.RealIndices.Contains((uint)transform)) continue;
            var hash = model.TransformNameHashes[transform];
            if (!result.Contains(hash)) result.Add(hash);
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
    private static int IndexOf(IReadOnlyList<int> values, int value) { for (var i = 0; i < values.Count; i++) if (values[i] == value) return i; return -1; }
}
