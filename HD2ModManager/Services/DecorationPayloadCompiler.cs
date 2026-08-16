using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModManager.Models;

namespace HD2ModManager.Services;

// Extracts selected readable mesh fragments into portable, non-patch payload files.
public sealed class DecorationPayloadCompiler
{
    private readonly IModFileResolver fileResolver;

    public DecorationPayloadCompiler(IModFileResolver fileResolver) => this.fileResolver = fileResolver;

    public async Task<IReadOnlyList<DecorationPayloadFile>> CompileAsync(
        ModNode source, string modsRootDirectory, IReadOnlyList<DecorationSourceUnit> selected,
        DecorationAttachmentPlan plan, string outputDirectory,
        IReadOnlyDictionary<string, IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>>? preparedEntries = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var staleFile in new[] { "stocky.bin", "slim.bin" })
        {
            var stalePath = Path.Combine(outputDirectory, staleFile);
            if (File.Exists(stalePath)) File.Delete(stalePath);
        }
        var requested = selected
            .GroupBy(item => (item.TypeId, item.FileId))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var resolvedSelections = new HashSet<(ulong TypeId, ulong FileId, int MeshInfoIndex, bool IsCulling)>();
        var fragments = new List<(string Variant, string Layer, DecorationMeshFragment Fragment)>();
        var reader = new PatchUnitMeshReader();
        var patchPaths = await fileResolver.ResolvePatchFilesAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);

        foreach (var patchPath in patchPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPatchPath = Path.GetFullPath(patchPath);
            var entries = preparedEntries is not null && preparedEntries.TryGetValue(fullPatchPath, out var cachedEntries)
                ? cachedEntries
                : await new PatchTocScanner().ScanEntriesAsync(fullPatchPath, cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries.Where(entry => requested.ContainsKey((entry.AssetKey.TypeId, entry.AssetKey.FileId))))
            {
                var unit = await reader.ReadAsync(entry, entries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
                var selections = requested[(entry.AssetKey.TypeId, entry.AssetKey.FileId)];
                var visibleSelections = selections.Where(selection => !selection.IsCulling).ToArray();
                if (visibleSelections.Length != 0)
                {
                    // The planning table exposes one representative mesh per user-facing Unit.
                    // Preserve every renderable LOD rather than treating that representative as the only mesh.
                    var representative = visibleSelections[0];
                    var lods = unit.Model.RawMeshData
                        .Select(raw => (Raw: raw, Mesh: unit.Model.Meshes.SingleOrDefault(mesh => mesh.Index == raw.MeshInfoIndex)))
                        .Where(item => item.Mesh is not null && IsVisibleLod(item.Raw, item.Mesh))
                        .OrderBy(item => item.Raw.LodIndex)
                        .ToArray();
                    if (lods.Length == 0) throw new InvalidDataException("Selected decoration Unit has no visible LOD geometry.");
                    foreach (var lod in lods)
                    {
                        fragments.Add((ToVariant(representative.BodyVariant), ToLayer(representative.Layer), CreateFragment(unit, lod.Mesh!, lod.Raw)));
                    }
                    foreach (var selection in visibleSelections)
                        resolvedSelections.Add((selection.TypeId, selection.FileId, selection.MeshInfoIndex, false));
                }

                foreach (var selection in selections.Where(selection => selection.IsCulling))
                {
                    var mesh = unit.Model.Meshes.SingleOrDefault(item => item.Index == selection.MeshInfoIndex)
                        ?? throw new InvalidDataException("Selected decoration culling mesh is missing.");
                    var rawMesh = unit.Model.RawMeshData.SingleOrDefault(item => item.MeshInfoIndex == selection.MeshInfoIndex)
                        ?? throw new InvalidDataException("Selected decoration culling mesh has no geometry.");
                    // Catalog classification can identify a culling body through a global bone name
                    // unavailable in this local re-read. Its native LOD marker is an equivalent fallback.
                    if (!mesh.SemanticInfo.IsCullingBody && rawMesh.LodIndex >= 0)
                        throw new InvalidDataException("Selected decoration mesh is not a culling mesh.");
                    fragments.Add((ToVariant(selection.BodyVariant), ToLayer(selection.Layer), CreateFragment(unit, mesh, rawMesh)));
                    resolvedSelections.Add((selection.TypeId, selection.FileId, selection.MeshInfoIndex, true));
                }
            }
        }
        var requestedSelections = selected.Select(selection => (selection.TypeId, selection.FileId, selection.MeshInfoIndex, selection.IsCulling)).ToHashSet();
        if (!requestedSelections.SetEquals(resolvedSelections)) throw new InvalidDataException("Some selected decoration Units could not be read.");

        var documents = BuildPayloads(fragments, plan);
        var output = new List<DecorationPayloadFile>();
        foreach (var document in documents)
        {
            var fileName = document.BodyVariant == "Stocky" ? "stocky.bin" : "slim.bin";
            await WriteAsync(Path.Combine(outputDirectory, fileName), document, cancellationToken).ConfigureAwait(false);
            output.Add(new DecorationPayloadFile { BodyVariant = document.BodyVariant, File = fileName });
        }
        return output;
    }

    private static IReadOnlyList<DecorationPayloadDocument> BuildPayloads(IReadOnlyList<(string Variant, string Layer, DecorationMeshFragment Fragment)> fragments, DecorationAttachmentPlan plan)
    {
        if (plan.TargetBodyVariant is "Stocky" or "Slim")
            return [CreatePayload(plan.TargetBodyVariant, fragments)];
        if (plan.DualVariantMode == "ApplyAllToBoth")
        {
            return [CreatePayload("Stocky", fragments), CreatePayload("Slim", fragments)];
        }
        var stocky = fragments.Where(item => item.Variant is "Stocky" or "Any").ToArray();
        var slim = fragments.Where(item => item.Variant is "Slim" or "Any").ToArray();
        if (stocky.Length == 0 || slim.Length == 0)
            throw new InvalidDataException("双身形自动分配需要 Slim 和 Stocky 来源；仅有单一身形时，请选择“来源全部附加到每一个身形”。");
        return [
            CreatePayload("Stocky", stocky),
            CreatePayload("Slim", slim)
        ];
    }

    private static DecorationPayloadDocument CreatePayload(string bodyVariant, IEnumerable<(string Variant, string Layer, DecorationMeshFragment Fragment)> fragments)
    {
        var items = fragments.ToArray();
        return new DecorationPayloadDocument
        {
            BodyVariant = bodyVariant,
            SourceLayers = items.Select(item => item.Layer).Where(layer => !string.IsNullOrWhiteSpace(layer)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Fragments = items.Select(item => item.Fragment).ToList()
        };
    }

    private static string ToVariant(string bodyVariant)
    {
        if (bodyVariant.Equals("Slim", StringComparison.OrdinalIgnoreCase)) return "Slim";
        if (bodyVariant.Equals("Stocky", StringComparison.OrdinalIgnoreCase)) return "Stocky";
        if (bodyVariant.Equals("Any", StringComparison.OrdinalIgnoreCase)) return "Any";
        throw new InvalidDataException("所选来源 Unit 的身形无法识别，请选择明确身形或 Any 的来源。");
    }

    private static string ToLayer(string layer)
        => layer is "Armor" or "Undergarment" or "Accessory" ? layer : string.Empty;

    private static bool IsVisibleLod(UnitRawMeshData rawMesh, UnitMeshInfo mesh)
        => rawMesh.LodIndex >= 0
            && mesh.SemanticInfo.IsVisualMesh
            && !mesh.SemanticInfo.IsCullingBody
            && !mesh.SemanticInfo.IsStaticMesh
            && (rawMesh.Vertices.Count > 3 || rawMesh.Triangles.Count > 1);

    private static DecorationMeshFragment CreateFragment(PatchUnitMesh unit, UnitMeshInfo mesh, UnitRawMeshData rawMesh)
    {
        var stream = unit.Model.Streams.SingleOrDefault(item => item.Index == mesh.StreamIndex)
            ?? throw new InvalidDataException("Selected decoration mesh has no stream.");
        return new DecorationMeshFragment
        {
            Mesh = mesh,
            RawMesh = rawMesh,
            Stream = stream,
            Materials = unit.Model.Materials.ToList(),
            BoneInfos = unit.Model.BoneInfos.ToList(),
            TransformInfo = unit.Model.TransformInfo,
            TransformNameHashes = unit.Model.TransformNameHashes.ToList()
        };
    }

    private static async Task WriteAsync(string path, DecorationPayloadDocument document, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        await JsonSerializer.SerializeAsync(gzip, document, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken).ConfigureAwait(false);
    }
}
