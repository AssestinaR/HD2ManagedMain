namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

public sealed record CanonicalDecorationFragment(
    UnitMeshInfo Mesh,
    UnitRawMeshData RawMesh,
    UnitStreamInfo Stream,
    IReadOnlyList<UnitMaterialBinding> Materials,
    IReadOnlyList<UnitBoneInfo> BoneInfos,
    UnitTransformInfo TransformInfo,
    IReadOnlyList<uint> TransformNameHashes);

public sealed record CanonicalDecorationAppendInput(CanonicalDecorationFragment Fragment, ulong MaterialNamespace);

public sealed record CanonicalDecorationAppendResult(PatchUnitMeshEditResult? Edit, IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
    public bool IsValid => Edit is not null && Diagnostics.Count == 0;
}

// Unit-level Blender Join pipeline. All sources are appended before material layout and palette
// compilation, so multiple decorations share one authoritative final Unit representation.
public sealed class CanonicalDecorationUnitAppender
{
    public CanonicalDecorationAppendResult TryAppend(
        PatchUnitMesh targetUnit, int targetMeshInfoIndex, CanonicalDecorationFragment fragment,
        ulong decorationNamespace, UnitTransformInfo avatarTransformInfo)
        => TryAppendMany(targetUnit, targetMeshInfoIndex, [new(fragment, decorationNamespace)], avatarTransformInfo);

    public CanonicalDecorationAppendResult TryAppendMany(
        PatchUnitMesh targetUnit, int targetMeshInfoIndex, IReadOnlyList<CanonicalDecorationAppendInput> inputs,
        UnitTransformInfo avatarTransformInfo)
    {
        ArgumentNullException.ThrowIfNull(targetUnit);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(avatarTransformInfo);
        if (inputs.Count == 0) return new(null, [new("DecorationSourcesMissing", "Decoration append requires at least one source mesh.")]);
        var diagnostics = new List<CanonicalPlanDiagnostic>();
        var targetRaw = targetUnit.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == targetMeshInfoIndex);
        var targetMesh = targetUnit.Model.Meshes.SingleOrDefault(mesh => mesh.Index == targetMeshInfoIndex);
        if (targetRaw is null || targetMesh is null) return new(null, [new("DecorationTargetMeshMissing", "The selected host Unit has no readable target mesh.")]);
        if (targetUnit.Model.CompositeRef != 0) return new(null, [new("DecorationCompositeUnitUnsupported", "Composite-backed host Units are not supported for decoration append.")]);
        if (targetRaw.LodIndex < 0) return new(null, [new("DecorationNonVisualLod", "Decoration append only supports visible LOD meshes.")]);
        if (inputs.Any(input => input.Fragment.RawMesh.LodIndex != targetRaw.LodIndex))
            return new(null, [new("DecorationLodMismatch", "A decoration LOD does not match the selected host LOD.")]);

        var sourceModels = inputs.Select(input => CreateSourceModel(input.Fragment)).ToArray();
        UnitMeshModel expanded;
        try
        {
            expanded = new CanonicalTransformInfoExpander().Expand(targetUnit.Model,
                sourceModels.Select((model, index) => (model, inputs[index].Fragment.RawMesh)), avatarTransformInfo);
        }
        catch (InvalidDataException exception) { return new(null, [new("DecorationTransformExpansionFailed", exception.Message)]); }

        var merged = targetRaw;
        var origins = targetRaw.Sections.Select((_, index) => new CanonicalAppendSectionOrigin(index, -1, index)).ToList();
        var materialProvenance = new List<CanonicalMaterialSectionProvenance>();
        for (var sourceIndex = 0; sourceIndex < inputs.Count; sourceIndex++)
        {
            var input = inputs[sourceIndex];
            var source = sourceModels[sourceIndex];
            var transform = new CanonicalTransformResolver().TryResolve(source, input.Fragment.RawMesh.MeshInfoIndex, expanded, targetMeshInfoIndex);
            diagnostics.AddRange(transform.Diagnostics);
            if (!transform.IsValid) return new(null, diagnostics);
            var appended = new CanonicalAppendMeshAssembler().TryAppend(merged, input.Fragment.RawMesh, transform.SourceToTargetLocal!.Value);
            diagnostics.AddRange(appended.Diagnostics);
            if (!appended.IsValid) return new(null, diagnostics);
            var start = merged.Sections.Count;
            var sourceBindings = BuildMaterialProvenance(source, input.Fragment.RawMesh, targetRaw, start, targetMeshInfoIndex, input.MaterialNamespace, diagnostics);
            if (diagnostics.Count != 0) return new(null, diagnostics);
            materialProvenance.AddRange(sourceBindings);
            origins.AddRange(input.Fragment.RawMesh.Sections.Select((_, section) => new CanonicalAppendSectionOrigin(start + section, sourceIndex, section)));
            merged = appended.Mesh!;
        }

        var provisional = expanded.RawMeshData.Select(raw => raw.MeshInfoIndex == targetMeshInfoIndex ? merged : raw).ToArray();
        var material = new CanonicalUnitMaterialLayoutCompiler().TryCompile(expanded, provisional, materialProvenance);
        diagnostics.AddRange(material.Diagnostics);
        if (!material.IsValid) return new(null, diagnostics);
        var finalMerged = material.Meshes.Single(raw => raw.MeshInfoIndex == targetMeshInfoIndex);
        var sources = sourceModels.Select((model, index) => new CanonicalAppendSource(model, inputs[index].Fragment.RawMesh)).ToArray();
        var bones = new CanonicalAppendBoneCompiler().TryCompile(expanded, targetRaw, sources, finalMerged, origins);
        diagnostics.AddRange(bones.Diagnostics);
        if (!bones.IsValid) return new(null, diagnostics);

        var finalRaw = material.Meshes.Select(raw => raw.MeshInfoIndex == targetMeshInfoIndex ? bones.Mesh! : raw).ToArray();
        var stream = new CanonicalStreamContractCompiler().TryCompile(expanded, finalRaw);
        diagnostics.AddRange(stream.Diagnostics);
        if (!stream.IsValid) return new(null, diagnostics);
        expanded = expanded with { Streams = stream.Streams };
        var prepared = new List<UnitRawMeshData>(finalRaw.Length);
        foreach (var raw in finalRaw)
        {
            var result = new CanonicalMeshPreparation().TryPrepare(raw, expanded.Streams.Single(item => item.Index == raw.StreamIndex));
            diagnostics.AddRange(result.Diagnostics);
            if (result.Mesh is not null) prepared.Add(result.Mesh);
        }
        if (diagnostics.Count != 0 || prepared.Count != finalRaw.Length) return new(null, diagnostics);
        var rebuiltBones = expanded.BoneInfos.Select((bone, index) => new CanonicalBoneInfoRebuild(index, index == targetRaw.LodIndex ? bones.BoneInfo! : bone)).ToArray();
        var rebuilt = new CanonicalUnitRebuilder().TryRebuild(expanded, targetUnit.Payload.TocData, prepared, rebuiltBones, material.Bindings);
        diagnostics.AddRange(rebuilt.Diagnostics);
        if (!rebuilt.IsValid) return new(null, diagnostics);
        return new(new PatchUnitMeshEditResult(targetUnit.Entry, targetUnit.Payload, rebuilt.Output!.TocData, rebuilt.Output.GpuData,
            ReplacementMaterialIds: material.Bindings.Select(binding => binding.MaterialId).Distinct().ToArray()), []);
    }

    private static IReadOnlyList<CanonicalMaterialSectionProvenance> BuildMaterialProvenance(
        UnitMeshModel source, UnitRawMeshData sourceRaw, UnitRawMeshData targetRaw, int finalSectionStart,
        int targetMeshInfoIndex, ulong materialNamespace, List<CanonicalPlanDiagnostic> diagnostics)
    {
        var resolved = CanonicalMaterialBindingResolver.Resolve(source, sourceRaw, targetRaw);
        diagnostics.AddRange(resolved.Diagnostics);
        if (!resolved.IsValid) return [];
        var result = new List<CanonicalMaterialSectionProvenance>();
        var visible = 0;
        for (var sectionIndex = 0; sectionIndex < sourceRaw.Sections.Count; sectionIndex++)
        {
            if (sourceRaw.Sections[sectionIndex].Triangles.Count == 0) continue;
            if (visible >= resolved.ResolvedSectionBindings.Count)
            {
                diagnostics.Add(new("DecorationMaterialBindingMismatch", "The decoration material sections could not be matched to its geometry."));
                break;
            }
            var binding = resolved.ResolvedSectionBindings[visible++];
            result.Add(new(targetMeshInfoIndex, finalSectionStart + sectionIndex, materialNamespace,
                binding.SourceSlotId, binding.PreferredTargetSlotId, binding.MaterialId, binding.UsesTargetUnitMaterialSlotLookup));
        }
        return result;
    }

    private static UnitMeshModel CreateSourceModel(CanonicalDecorationFragment fragment)
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, fragment.BoneInfos, [fragment.Stream], [fragment.Mesh], fragment.Materials, [], [fragment.RawMesh])
        { TransformInfo = fragment.TransformInfo, TransformNameHashes = fragment.TransformNameHashes };
}
