using HD2ModAdaptation.PatchReconstruction.PatchWorkspace;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Rebuilds one same-AssetKey Unit with the current Canonical pipeline without using the legacy SDK-style chain.
public sealed record SameKeyCanonicalUnitRebuildRequest(
    PatchUnitMesh Source,
    GameDataUnitMesh Target,
    IReadOnlyList<TargetShellMeshMapping> MeshMappings)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Target);
        ArgumentNullException.ThrowIfNull(MeshMappings);
        if (Source.Entry.AssetKey != Target.AssetKey)
            throw new InvalidDataException("Canonical same-key rebuilding requires identical source and target AssetKeys.");
        if (MeshMappings.Any(mapping => mapping.SourceUnitAssetKey != Source.Entry.AssetKey))
            throw new InvalidDataException("A same-key Canonical mapping references another source Unit.");
        if (MeshMappings.Select(mapping => mapping.TargetMeshInfoIndex).Distinct().Count() != MeshMappings.Count)
            throw new InvalidDataException("A same-key Canonical target mesh has multiple source mappings.");
    }
}

public sealed record SameKeyCanonicalUnitRebuildResult(
    PatchWorkspaceJobResult? Job,
    int ReplacedMeshCount,
    int HiddenMeshCount,
    IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
    public bool IsValid => Job is { IsValid: true } && Diagnostics.Count == 0;
}

public sealed class SameKeyCanonicalUnitRebuilder
{
    private readonly CanonicalTransformResolver transformResolver = new();
    private readonly CanonicalBoneRebuilder boneRebuilder = new();
    private readonly CanonicalMeshSemanticMerger merger = new();
    private readonly CanonicalMeshPreparation preparation = new();
    private readonly CanonicalPlaceholderMinifier minifier = new();
    private readonly CanonicalLodBonePaletteCompiler paletteCompiler = new();
    private readonly CanonicalStreamContractCompiler streamCompiler = new();
    private readonly CanonicalUnitRebuilder unitRebuilder = new();

    public SameKeyCanonicalUnitRebuildResult Rebuild(SameKeyCanonicalUnitRebuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        using var positionDiagnostics = CanonicalPositionDiagnostics.BeginUnit(request.Target.AssetKey.FileId);
        var diagnostics = new List<CanonicalPlanDiagnostic>();
        var mappings = request.MeshMappings.ToDictionary(mapping => mapping.TargetMeshInfoIndex);
        var finalMeshes = new List<UnitRawMeshData>(request.Target.Model.Meshes.Count);
        var provisionalByMesh = new Dictionary<int, UnitBoneInfo>();
        var sourceMaterialByTargetSlot = new Dictionary<uint, ulong>();
        var hiddenCount = 0;
        var replacedCount = 0;

        foreach (var targetMesh in request.Target.Model.Meshes)
        {
            var targetRaw = FindRaw(request.Target.Model, targetMesh.Index, diagnostics, "target");
            if (targetRaw is null) continue;
            var targetStream = request.Target.Model.Streams.SingleOrDefault(stream => stream.Index == (int)targetRaw.StreamIndex);
            if (targetStream is null) continue;

            if (!mappings.TryGetValue(targetMesh.Index, out var mapping))
            {
                var hidden = minifier.TryMinify(targetRaw, targetStream);
                diagnostics.AddRange(hidden.Diagnostics);
                if (hidden.Mesh is not null) finalMeshes.Add(hidden.Mesh);
                hiddenCount++;
                continue;
            }

            var sourceRaw = FindRaw(request.Source.Model, mapping.SourceMeshInfoIndex, diagnostics, "source");
            if (sourceRaw is null) continue;
            CanonicalPositionDiagnostics.RecordMesh("source", sourceRaw, targetStream);
            var transform = transformResolver.TryResolve(request.Source.Model, sourceRaw.MeshInfoIndex, request.Target.Model, targetRaw.MeshInfoIndex);
            diagnostics.AddRange(transform.Diagnostics);
            if (!transform.IsValid) continue;

            UnitRawMeshData preparedSource = sourceRaw;
            UnitBoneInfo? provisionalBone = null;
            if (HasBoneData(request.Source.Model, sourceRaw))
            {
                var rebuiltBone = boneRebuilder.TryRebuild(request.Source.Model, sourceRaw, request.Target.Model, targetRaw);
                diagnostics.AddRange(rebuiltBone.Diagnostics);
                if (!rebuiltBone.IsValid || rebuiltBone.Mesh is null || rebuiltBone.BoneInfo is null) continue;
                preparedSource = rebuiltBone.Mesh;
                CanonicalPositionDiagnostics.RecordMesh("after-bone-rebuild", preparedSource, targetStream);
                provisionalBone = rebuiltBone.BoneInfo;
            }

            var merged = merger.TryMerge(
                new CanonicalMeshSemanticMergeRequest(
                    new(request.Source.Entry.AssetKey, sourceRaw.MeshInfoIndex),
                    new(request.Target.AssetKey, targetRaw.MeshInfoIndex),
                    transform.SourceToTargetLocal!.Value),
                targetRaw,
                preparedSource);
            diagnostics.AddRange(merged.Diagnostics);
            if (!merged.IsValid || merged.Mesh is null) continue;

            foreach (var binding in CollectMappedMaterialBindings(request.Source.Model, sourceRaw, targetRaw))
            {
                if (sourceMaterialByTargetSlot.TryGetValue(binding.SectionId, out var existing) && existing != binding.MaterialId)
                    diagnostics.Add(new("SameKeyMaterialSlotConflict", $"Target material slot {binding.SectionId} maps to source materials 0x{existing:x16} and 0x{binding.MaterialId:x16}."));
                else
                    sourceMaterialByTargetSlot[binding.SectionId] = binding.MaterialId;
            }

            CanonicalPositionDiagnostics.RecordMesh("merged", merged.Mesh, targetStream);

            finalMeshes.Add(merged.Mesh);
            if (provisionalBone is not null)
                provisionalByMesh[targetRaw.MeshInfoIndex] = provisionalBone;
            replacedCount++;
        }

        if (diagnostics.Count != 0)
            return new(null, replacedCount, hiddenCount, diagnostics);
        if (finalMeshes.Count != request.Target.Model.Meshes.Count)
            return Failure("SameKeyCanonicalMeshCoverage", "Canonical same-key rebuilding did not produce one final RawMesh for every target MeshInfo.", replacedCount, hiddenCount);

        var targetModel = request.Target.Model;
        var streams = streamCompiler.TryCompile(targetModel, finalMeshes);
        if (!streams.IsValid) return Failure(streams.Diagnostics, replacedCount, hiddenCount);
        targetModel = targetModel with { Streams = streams.Streams };
        var preparedMeshes = new List<UnitRawMeshData>(finalMeshes.Count);
        foreach (var raw in finalMeshes)
        {
            var stream = targetModel.Streams.Single(stream => stream.Index == (int)raw.StreamIndex);
            var prepared = preparation.TryPrepare(raw, stream);
            if (!prepared.IsValid || prepared.Mesh is null) return Failure(prepared.Diagnostics, replacedCount, hiddenCount);
            CanonicalPositionDiagnostics.RecordMesh("prepared", prepared.Mesh, stream);
            preparedMeshes.Add(prepared.Mesh);
        }
        finalMeshes = preparedMeshes;

        var provisional = new List<CanonicalLodBoneInput>();
        foreach (var raw in finalMeshes)
            if (provisionalByMesh.TryGetValue(raw.MeshInfoIndex, out var provisionalBone))
                provisional.Add(new CanonicalLodBoneInput(raw, provisionalBone));

        var rebuiltBones = targetModel.BoneInfos.Select((bone, index) => new CanonicalBoneInfoRebuild(index, bone)).ToDictionary(item => item.LodIndex);
        foreach (var group in provisional.GroupBy(item => item.Mesh.LodIndex))
        {
            var compiled = paletteCompiler.TryCompile(targetModel, group.Key, group.ToArray());
            if (!compiled.IsValid || compiled.BoneInfo is null)
                return Failure(compiled.Diagnostics, replacedCount, hiddenCount);
            rebuiltBones[group.Key] = new CanonicalBoneInfoRebuild(group.Key, compiled.BoneInfo);
            var byMesh = compiled.Meshes.ToDictionary(mesh => mesh.MeshInfoIndex);
            for (var index = 0; index < finalMeshes.Count; index++)
                if (byMesh.TryGetValue(finalMeshes[index].MeshInfoIndex, out var rewritten)) finalMeshes[index] = rewritten;
        }

        // Palette compilation rewrites bone-index components and intentionally clears
        // RawVertexRecord.Data. Re-encode every final mesh against the compiled stream
        // contract before CanonicalUnitRebuilder appends vertex bytes to the GPU buffer.
        var reencodedMeshes = new List<UnitRawMeshData>(finalMeshes.Count);
        foreach (var raw in finalMeshes)
        {
            var stream = targetModel.Streams.Single(stream => stream.Index == (int)raw.StreamIndex);
            var prepared = preparation.TryPrepare(raw, stream);
            if (!prepared.IsValid || prepared.Mesh is null)
                return Failure(prepared.Diagnostics, replacedCount, hiddenCount);
            CanonicalPositionDiagnostics.RecordMesh("prepared-after-palette", prepared.Mesh, stream);
            reencodedMeshes.Add(prepared.Mesh);
        }
        finalMeshes = reencodedMeshes;

        var finalMaterialBindings = targetModel.Materials
            .Where(binding => !sourceMaterialByTargetSlot.ContainsKey(binding.SectionId))
            .Concat(sourceMaterialByTargetSlot.Select(binding => new UnitMaterialBinding(binding.Key, binding.Value)))
            .ToArray();
        if (diagnostics.Count != 0)
            return new(null, replacedCount, hiddenCount, diagnostics);

        var rebuilt = unitRebuilder.TryRebuild(targetModel, request.Target.Payload.TocData, finalMeshes, rebuiltBones.Values.ToArray(), finalMaterialBindings);
        if (!rebuilt.IsValid || rebuilt.Output is null)
            return Failure(rebuilt.Diagnostics, replacedCount, hiddenCount);
        var entry = new CanonicalPatchSessionEntry(
            request.Target.AssetKey,
            CanonicalPatchEntryOwnership.TargetOutput,
            rebuilt.Output.TocData,
            rebuilt.Output.GpuData,
            Array.Empty<byte>(),
            request.Target.Payload.Entry.Unknown1,
            request.Target.Payload.Entry.Unknown2,
            request.Target.Payload.Entry.Unknown3,
            request.Target.Payload.Entry.Unknown4);
        return new(PatchWorkspaceJobResult.Unit(entry, $"0x{request.Target.AssetKey.FileId:x16}"), replacedCount, hiddenCount, Array.Empty<CanonicalPlanDiagnostic>());
    }

    private static UnitRawMeshData? FindRaw(UnitMeshModel model, int index, List<CanonicalPlanDiagnostic> diagnostics, string role)
    {
        var matches = model.RawMeshData.Where(raw => raw.MeshInfoIndex == index).ToArray();
        if (matches.Length != 1)
        {
            diagnostics.Add(new("RawMeshCardinality", $"The {role} Unit must contain exactly one RawMesh for MeshInfo {index}."));
            return null;
        }
        return matches[0];
    }

    private static bool HasBoneData(UnitMeshModel model, UnitRawMeshData mesh)
        => mesh.LodIndex >= 0 && mesh.LodIndex < model.BoneInfos.Count && model.BoneInfos[mesh.LodIndex].RealIndices.Count > 0;

    private static IReadOnlyList<UnitMaterialBinding> CollectMappedMaterialBindings(UnitMeshModel source, UnitRawMeshData sourceRaw, UnitRawMeshData targetRaw)
    {
        var sourceMesh = source.Meshes.Single(mesh => mesh.Index == sourceRaw.MeshInfoIndex);
        var visibleSections = sourceRaw.Sections.Where(section => section.Triangles.Count != 0).ToArray();
        var result = new List<UnitMaterialBinding>();
        for (var index = 0; index < visibleSections.Length && index < targetRaw.Sections.Count; index++)
        {
            var sourceSection = visibleSections[index];
            if (sourceSection.MaterialIndex >= sourceMesh.MaterialSlotIds.Count) continue;
            var sourceSlot = sourceMesh.MaterialSlotIds[(int)sourceSection.MaterialIndex];
            var sourceMaterial = source.Materials
                .Where(binding => binding.SectionId == sourceSlot)
                .Select(binding => (ulong?)binding.MaterialId)
                .FirstOrDefault();
            if (sourceMaterial is { } material)
                result.Add(new UnitMaterialBinding(targetRaw.Sections[index].MaterialSlotId, material));
        }
        return result;
    }

    private static SameKeyCanonicalUnitRebuildResult Failure(string code, string message, int replaced, int hidden)
        => new(null, replaced, hidden, [new(code, message)]);

    private static SameKeyCanonicalUnitRebuildResult Failure(IReadOnlyList<CanonicalPlanDiagnostic> diagnostics, int replaced, int hidden)
        => new(null, replaced, hidden, diagnostics);
}
