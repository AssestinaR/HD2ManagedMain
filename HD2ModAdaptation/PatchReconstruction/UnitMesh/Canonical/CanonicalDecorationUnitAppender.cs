namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Unit-level Blender Join pipeline for one explicitly selected decoration mesh. This intentionally
// preserves the target shell topology and delegates final serialization to CanonicalUnitRebuilder.
public sealed record CanonicalDecorationFragment(
    UnitMeshInfo Mesh,
    UnitRawMeshData RawMesh,
    UnitStreamInfo Stream,
    IReadOnlyList<UnitMaterialBinding> Materials,
    IReadOnlyList<UnitBoneInfo> BoneInfos,
    UnitTransformInfo TransformInfo,
    IReadOnlyList<uint> TransformNameHashes);

public sealed record CanonicalDecorationAppendResult(
    PatchUnitMeshEditResult? Edit,
    IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
    public bool IsValid => Edit is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalDecorationUnitAppender
{
    public CanonicalDecorationAppendResult TryAppend(
        PatchUnitMesh targetUnit,
        int targetMeshInfoIndex,
        CanonicalDecorationFragment fragment,
        ulong decorationNamespace,
        UnitTransformInfo avatarTransformInfo)
    {
        ArgumentNullException.ThrowIfNull(targetUnit);
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(avatarTransformInfo);
        var diagnostics = new List<CanonicalPlanDiagnostic>();
        var targetRaw = targetUnit.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == targetMeshInfoIndex);
        var targetMesh = targetUnit.Model.Meshes.SingleOrDefault(mesh => mesh.Index == targetMeshInfoIndex);
        if (targetRaw is null || targetMesh is null)
            return new(null, [new("DecorationTargetMeshMissing", "The selected host Unit has no readable target mesh.")]);
        if (targetUnit.Model.CompositeRef != 0)
            return new(null, [new("DecorationCompositeUnitUnsupported", "Composite-backed host Units are not supported for decoration append.")]);
        if (targetRaw.LodIndex != fragment.RawMesh.LodIndex)
            return new(null, [new("DecorationLodMismatch", "The decoration LOD does not match the selected host LOD.")]);
        if (targetRaw.LodIndex < 0 || fragment.RawMesh.LodIndex < 0)
            return new(null, [new("DecorationNonVisualLod", "Decoration append only supports visible LOD meshes.")]);

        var source = CreateSourceModel(fragment);
        UnitMeshModel expanded;
        try { expanded = new CanonicalTransformInfoExpander().Expand(targetUnit.Model, [(source, fragment.RawMesh)], avatarTransformInfo); }
        catch (InvalidDataException exception) { return new(null, [new("DecorationTransformExpansionFailed", exception.Message)]); }
        var transform = new CanonicalTransformResolver().TryResolve(source, fragment.RawMesh.MeshInfoIndex, expanded, targetMeshInfoIndex);
        diagnostics.AddRange(transform.Diagnostics);
        if (!transform.IsValid) return new(null, diagnostics);
        var appended = new CanonicalAppendMeshAssembler().TryAppend(targetRaw, fragment.RawMesh, transform.SourceToTargetLocal!.Value);
        diagnostics.AddRange(appended.Diagnostics);
        if (!appended.IsValid) return new(null, diagnostics);

        var materialProvenance = BuildMaterialProvenance(source, fragment.RawMesh, targetRaw, appended.Sections, decorationNamespace, diagnostics);
        if (diagnostics.Count != 0) return new(null, diagnostics);
        var provisional = expanded.RawMeshData.Select(raw => raw.MeshInfoIndex == targetMeshInfoIndex ? appended.Mesh! : raw).ToArray();
        var material = new CanonicalUnitMaterialLayoutCompiler().TryCompile(expanded, provisional, materialProvenance);
        diagnostics.AddRange(material.Diagnostics);
        if (!material.IsValid) return new(null, diagnostics);
        var finalAppended = material.Meshes.Single(raw => raw.MeshInfoIndex == targetMeshInfoIndex);
        var bones = new CanonicalAppendBoneCompiler().TryCompile(expanded, targetRaw, source, fragment.RawMesh, finalAppended, appended.Sections);
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
            var streamInfo = expanded.Streams.Single(item => item.Index == raw.StreamIndex);
            var result = new CanonicalMeshPreparation().TryPrepare(raw, streamInfo);
            diagnostics.AddRange(result.Diagnostics);
            if (result.Mesh is not null) prepared.Add(result.Mesh);
        }
        if (diagnostics.Count != 0 || prepared.Count != finalRaw.Length) return new(null, diagnostics);
        var rebuiltBones = expanded.BoneInfos.Select((bone, index) => new CanonicalBoneInfoRebuild(index, index == targetRaw.LodIndex ? bones.BoneInfo! : bone)).ToArray();
        var rebuilt = new CanonicalUnitRebuilder().TryRebuild(expanded, targetUnit.Payload.TocData, prepared, rebuiltBones, material.Bindings);
        diagnostics.AddRange(rebuilt.Diagnostics);
        if (!rebuilt.IsValid) return new(null, diagnostics);
        var edit = new PatchUnitMeshEditResult(targetUnit.Entry, targetUnit.Payload, rebuilt.Output!.TocData, rebuilt.Output.GpuData,
            ReplacementMaterialIds: material.Bindings.Select(binding => binding.MaterialId).Distinct().ToArray());
        return new(edit, []);
    }

    private static IReadOnlyList<CanonicalMaterialSectionProvenance> BuildMaterialProvenance(
        UnitMeshModel source, UnitRawMeshData sourceRaw, UnitRawMeshData targetRaw,
        IReadOnlyList<CanonicalAppendSectionProvenance> sections, ulong decorationNamespace, List<CanonicalPlanDiagnostic> diagnostics)
    {
        var resolved = CanonicalMaterialBindingResolver.Resolve(source, sourceRaw, targetRaw);
        diagnostics.AddRange(resolved.Diagnostics);
        if (!resolved.IsValid) return [];
        var bindings = resolved.ResolvedSectionBindings;
        var result = new List<CanonicalMaterialSectionProvenance>();
        var visible = 0;
        foreach (var section in sections.Where(section => !section.IsTargetSection))
        {
            if (sourceRaw.Sections[section.SourceSectionIndex].Triangles.Count == 0) continue;
            if (visible >= bindings.Count)
            {
                diagnostics.Add(new("DecorationMaterialBindingMismatch", "The decoration material sections could not be matched to its geometry."));
                break;
            }
            var binding = bindings[visible++];
            result.Add(new(targetRaw.MeshInfoIndex,
                section.FinalSectionIndex, decorationNamespace, binding.SourceSlotId, binding.PreferredTargetSlotId, binding.MaterialId, binding.UsesTargetUnitMaterialSlotLookup));
        }
        return result;
    }

    private static UnitMeshModel CreateSourceModel(CanonicalDecorationFragment fragment)
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, fragment.BoneInfos, [fragment.Stream], [fragment.Mesh], fragment.Materials, [], [fragment.RawMesh])
        { TransformInfo = fragment.TransformInfo, TransformNameHashes = fragment.TransformNameHashes };
}
