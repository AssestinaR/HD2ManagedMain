using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Models;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using CoreAssetKey = HD2ModCore.Domain.AssetKey;

namespace HD2ModManager.Services;

// Rebuilds a host's generated Overlay only from its source patches. The marker prevents this
// service from ever deleting a user-created directory that merely happens to be named Overwrite.
public sealed class DecorationAttachmentService
{
    private const string MarkerName = ".hd2-decoration-overwrite";
    private readonly IEquipmentUnitCatalogService _catalog;

    public DecorationAttachmentService(StoragePaths paths)
    {
        _catalog = CoreServices.CreateEquipmentUnitCatalogService(paths);
    }

    public async Task RebuildHostAsync(ModNode host, IEnumerable<(ModEntity Mod, DecorationPlanDocument Plan)> enabled, string modsRoot, string gameDataDirectory, CancellationToken cancellationToken = default)
    {
        var attachments = enabled.ToArray();
        var output = Path.Combine(modsRoot, host.RelativePath, "Overwrite");
        LogService.Info($"装饰合并开始：主体={host.Id.Value:N}，装饰数={attachments.Length}，输出={output}");
        if (attachments.Length == 0)
        {
            RemoveGeneratedOverwrite(output);
            LogService.Info($"装饰合并清理完成：主体={host.Id.Value:N}");
            return;
        }
        if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
            throw new InvalidOperationException("请在设置页重建资产索引。");
        var avatar = await new CanonicalAvatarRigReader(new AdaptationGameDataPackageResolver(gameDataDirectory)).ReadTransformInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var payloads = new List<(ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)>();
        foreach (var attachment in attachments)
        {
            foreach (var file in attachment.Plan.Payloads)
            {
                LogService.Info($"装饰合并读取 payload：装饰={attachment.Mod.Guid}，文件={file.File}，身形={file.BodyVariant}");
                var payload = await ReadPayloadAsync(Path.Combine(modsRoot, attachment.Mod.SourcePath ?? string.Empty, file.File), cancellationToken).ConfigureAwait(false);
                payloads.Add((attachment.Mod, attachment.Plan.Plan, payload));
            }
        }
        var backup = PrepareGeneratedOverwrite(output);
        try
        {
            var patchPaths = ResolveOriginalPatchFiles(host, modsRoot);
            LogService.Info($"装饰合并主体 Patch：主体={host.Id.Value:N}，数量={patchPaths.Count}");
            var edits = 0;
            foreach (var patch in patchPaths)
                edits += await RebuildPatchAsync(patch, output, payloads, avatar, cancellationToken).ConfigureAwait(false);
            if (edits == 0) throw new InvalidDataException("没有找到与装饰计划匹配的主体 Unit。");
            await File.WriteAllTextAsync(Path.Combine(output, MarkerName), "generated", cancellationToken).ConfigureAwait(false);
            if (backup is not null) Directory.Delete(backup, recursive: true);
            LogService.Info($"装饰合并完成：主体={host.Id.Value:N}，Unit 编辑数={edits}，输出={output}");
        }
        catch (Exception exception)
        {
            LogService.Error($"装饰合并失败：主体={host.Id.Value:N}，输出={output}，异常={exception}");
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            if (backup is not null && Directory.Exists(backup)) Directory.Move(backup, output);
            throw;
        }
    }

    private async Task<int> RebuildPatchAsync(string patch, string output, IReadOnlyList<(ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)> attachments, UnitTransformInfo avatar, CancellationToken cancellationToken)
    {
        LogService.Info($"装饰合并读取 Patch：{patch}");
        var entries = await new AdaptationPatchTocScanner().ScanEntriesAsync(patch, cancellationToken).ConfigureAwait(false);
        var keys = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId)
            .Select(entry => new HD2ModCore.Domain.AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId)).ToHashSet();
        var catalog = await _catalog.GetEntriesAsync(keys, cancellationToken).ConfigureAwait(false);
        var candidates = catalog
            .SelectMany(entry => entry.Parts.Select(part => new DecorationHostCandidate(entry.ArchiveId, part)))
            .GroupBy(candidate => (candidate.ArchiveId, candidate.Part.UnitAssetKey, candidate.Part.MeshInfoIndex))
            .Select(group => group.OrderByDescending(candidate => candidate.Part.StoredBytes).ThenByDescending(candidate => candidate.Part.Confidence).First())
            .ToArray();
        var geometryByMesh = await ReadCandidateGeometryFactsAsync(entries, candidates, cancellationToken).ConfigureAwait(false);
        candidates = candidates.Select(candidate => candidate with
        {
            Geometry = geometryByMesh.GetValueOrDefault((candidate.Part.UnitAssetKey, candidate.Part.MeshInfoIndex))
        }).ToArray();
        var targets = ResolveTargets(candidates, attachments);
        var targetsByUnit = targets
            .GroupBy(target => target.Part.UnitAssetKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var edits = new List<PatchUnitMeshEditResult>();
        var reader = new PatchUnitMeshReader();
        foreach (var entry in entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId))
        {
            var unitKey = new HD2ModCore.Domain.AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId);
            if (!targetsByUnit.TryGetValue(unitKey, out var matching)) continue;
            var targetMeshes = matching.Select(target => target.Part.MeshInfoIndex).Distinct().ToArray();
            if (targetMeshes.Length != 1)
                throw new InvalidDataException($"Unit 0x{entry.AssetKey.FileId:x16} resolved to {targetMeshes.Length} decoration host meshes; choose separate target parts.");
            var targetPart = matching[0].Part;
            var unit = await reader.ReadAsync(entry, entries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
            var targetRaw = unit.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == targetPart.MeshInfoIndex)
                ?? throw new InvalidDataException($"Target MeshInfo {targetPart.MeshInfoIndex} has no readable RawMesh.");
            var targetMesh = unit.Model.Meshes.Single(mesh => mesh.Index == targetPart.MeshInfoIndex);
            var selectedAttachments = matching
                .GroupBy(target => (target.Attachment.Mod.Guid, target.Attachment.Payload.BodyVariant), StringTupleComparer.OrdinalIgnoreCase)
                .Select(group => group.First().Attachment)
                .ToArray();
            var inputsByTargetMesh = new Dictionary<int, IReadOnlyList<CanonicalDecorationAppendInput>>();
            foreach (var lodTarget in ResolveVisibleLodFamily(unit.Model, targetRaw, targetMesh))
            {
                var inputs = selectedAttachments
                    .SelectMany(attachment => attachment.Payload.Fragments
                        .Where(fragment => fragment.RawMesh.LodIndex == lodTarget.Raw.LodIndex)
                        .Select(fragment => new CanonicalDecorationAppendInput(ToCanonical(fragment), DecorationNamespace(attachment.Mod.Guid))))
                    .ToArray();
                if (inputs.Length != 0) inputsByTargetMesh.Add(lodTarget.Mesh.Index, inputs);
            }
            if (inputsByTargetMesh.Count == 0)
            {
                var available = string.Join(",", selectedAttachments.SelectMany(attachment => attachment.Payload.Fragments).Select(fragment => fragment.RawMesh.LodIndex).Distinct().OrderBy(value => value));
                throw new InvalidDataException($"装饰没有与目标 LOD {targetRaw.LodIndex} 对应的来源 Mesh（可用 LOD：{available}）。");
            }
            var allInputs = inputsByTargetMesh.Values.SelectMany(inputs => inputs).ToArray();
            LogService.Info($"装饰合并 Unit：Patch={Path.GetFileName(patch)}，目标=0x{entry.AssetKey.FileId:x16}，Mesh={targetPart.MeshInfoIndex}，目标LOD=[{string.Join(",", inputsByTargetMesh.Keys.Select(index => unit.Model.RawMeshData.Single(raw => raw.MeshInfoIndex == index).LodIndex).OrderBy(lod => lod))}]，部位={targetPart.PartKind}，身形={targetPart.BodyVariant}，来源数={allInputs.Length}，来源顶点={allInputs.Sum(input => input.Fragment.RawMesh.Vertices.Count)}，来源三角={allInputs.Sum(input => input.Fragment.RawMesh.Triangles.Count)}");
            var result = new CanonicalDecorationUnitAppender().TryAppendLodFamily(unit, inputsByTargetMesh, avatar);
            if (!result.IsValid)
            {
                var detail = string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
                LogService.Error($"装饰合并 Unit 失败：目标=0x{entry.AssetKey.FileId:x16}，Mesh={targetPart.MeshInfoIndex}，问题={detail}");
                throw new InvalidDataException(detail);
            }
            edits.Add(result.Edit!);
        }
        if (edits.Count != 0)
        {
            LogService.Info($"装饰合并写出 Patch：源={patch}，Unit 编辑数={edits.Count}");
            await new PatchArchiveWriter().WriteAsync(patch, output, edits, overwriteExisting: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return edits.Count;
    }

    private static async Task<IReadOnlyDictionary<(CoreAssetKey Unit, int Mesh), UnitMeshGeometryFact>> ReadCandidateGeometryFactsAsync(
        IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry> entries,
        IReadOnlyList<DecorationHostCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var candidateKeys = candidates.Select(candidate => candidate.Part.UnitAssetKey).ToHashSet();
        var facts = new Dictionary<(CoreAssetKey Unit, int Mesh), UnitMeshGeometryFact>();
        var reader = new PatchUnitMeshReader();
        foreach (var entry in entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId && candidateKeys.Contains(new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId))))
        {
            try
            {
                var unit = await reader.ReadAsync(entry, entries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
                var unitKey = new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId);
                foreach (var fact in UnitGeometryFactsBuilder.Analyze(unit.Model).Meshes)
                    facts[(unitKey, fact.MeshInfoIndex)] = fact;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
            {
                LogService.Info($"装饰宿主几何分析跳过不可读 Unit：0x{entry.AssetKey.FileId:x16}，原因={exception.Message}");
            }
        }
        return facts;
    }

    private static IReadOnlyList<DecorationResolvedTarget> ResolveTargets(
        IReadOnlyList<DecorationHostCandidate> candidates,
        IReadOnlyList<(ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)> attachments)
    {
        var resolved = new List<DecorationResolvedTarget>();
        foreach (var attachmentGroup in attachments.GroupBy(attachment => attachment.Mod.Guid, StringComparer.OrdinalIgnoreCase))
        {
            var attachmentSet = attachmentGroup.ToArray();
            var plan = attachmentSet[0].Plan;
            foreach (var archiveGroup in candidates
                .Where(candidate => IsRenderableHost(candidate.Part) && PartMatches(candidate.Part.PartKind, plan.TargetPart))
                .GroupBy(candidate => candidate.ArchiveId, StringComparer.OrdinalIgnoreCase))
            {
                var archiveCandidates = archiveGroup.ToArray();
                var sourceLayers = ReadSourceLayers(attachmentSet);
                var selections = SelectHosts(archiveCandidates, attachmentSet, plan, sourceLayers);

                if (selections.Count == 0) continue;
                var sourceLayerText = sourceLayers.Count == 0 ? "legacy-fallback" : string.Join(",", sourceLayers);
                LogService.Info($"装饰宿主选择：装饰={attachmentSet[0].Mod.Guid}，Archive={archiveGroup.Key}，部位={plan.TargetPart}，来源层=[{sourceLayerText}]，候选=[{string.Join(", ", archiveCandidates.Select(DescribeCandidate))}]，选中=[{string.Join(", ", selections.Select(selection => $"{DescribeCandidate(selection.Candidate)}<-{selection.Attachment.Payload.BodyVariant}"))}]");
                resolved.AddRange(selections.Select(selection => new DecorationResolvedTarget(selection.Candidate.Part, selection.Attachment)));
            }
        }
        return resolved;
    }

    // Geometry is the first host gate. Layer and body policy only rank candidates
    // within the real-geometry set; otherwise a hidden placeholder would block an
    // actual host simply because it happens to share the source layer.
    private static IReadOnlyList<(DecorationHostCandidate Candidate, (ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload) Attachment)> SelectHosts(
        IReadOnlyList<DecorationHostCandidate> candidates,
        IReadOnlyList<(ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)> attachments,
        DecorationAttachmentPlan plan,
        IReadOnlySet<UnitMeshPartLayer> sourceLayers)
    {
        var realGeometry = candidates.Where(HasRenderableGeometry).ToArray();
        var geometryEligible = realGeometry.Length != 0 ? realGeometry : candidates.ToArray();
        var sameLayer = sourceLayers.Count == 0
            ? geometryEligible
            : geometryEligible.Where(candidate => sourceLayers.Contains(candidate.Part.Layer)).ToArray();
        var ranked = sameLayer.Length != 0 ? sameLayer : geometryEligible;
        var stockyPayload = FindPayload(attachments, "Stocky");
        var slimPayload = FindPayload(attachments, "Slim");
        var stocky = ChooseLargest(ranked, UnitMeshBodyVariant.Stocky);
        var slim = ChooseLargest(ranked, UnitMeshBodyVariant.Slim);
        var selections = new List<(DecorationHostCandidate Candidate, (ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload) Attachment)>();

        if (stocky is not null && slim is not null)
        {
            AddIfAvailable(selections, stocky, stockyPayload);
            AddIfAvailable(selections, slim, slimPayload);
            if (selections.Count != 0) return selections;
        }

        var universalPayload = FindPayload(attachments, "Any")
            ?? FindPayload(attachments, plan.TargetBodyVariant)
            ?? stockyPayload
            ?? slimPayload;
        var any = ChooseLargest(ranked, UnitMeshBodyVariant.Any);
        if (any is not null && universalPayload is not null)
        {
            selections.Clear();
            AddIfAvailable(selections, any, universalPayload);
            return selections;
        }

        // When the matching layer has only one concrete body, retain the CrossArmor
        // completion behavior: look through the remaining layers for its counterpart
        // before accepting a single-body host.
        if (stocky is not null && slimPayload is not null)
            slim = ChooseLargest(geometryEligible.Where(candidate => candidate.Part.UnitAssetKey != stocky.Part.UnitAssetKey), UnitMeshBodyVariant.Slim);
        if (slim is not null && stockyPayload is not null)
            stocky = ChooseLargest(geometryEligible.Where(candidate => candidate.Part.UnitAssetKey != slim.Part.UnitAssetKey), UnitMeshBodyVariant.Stocky);
        if (stocky is not null && slim is not null)
        {
            selections.Clear();
            AddIfAvailable(selections, stocky, stockyPayload);
            AddIfAvailable(selections, slim, slimPayload);
            if (selections.Count != 0) return selections;
        }

        var crossLayerAny = ChooseLargest(geometryEligible, UnitMeshBodyVariant.Any);
        if (crossLayerAny is not null && universalPayload is not null)
        {
            selections.Clear();
            AddIfAvailable(selections, crossLayerAny, universalPayload);
            return selections;
        }

        selections.Clear();
        AddIfAvailable(selections, stocky, stockyPayload);
        AddIfAvailable(selections, slim, slimPayload);
        return selections;
    }

    private static void AddIfAvailable(
        ICollection<(DecorationHostCandidate Candidate, (ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload) Attachment)> selections,
        DecorationHostCandidate? candidate,
        (ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)? attachment)
    {
        if (candidate is not null && attachment is not null) selections.Add((candidate, attachment.Value));
    }

    private static DecorationHostCandidate? ChooseLargest(IEnumerable<DecorationHostCandidate> candidates, UnitMeshBodyVariant bodyVariant)
        => candidates.Where(candidate => candidate.Part.BodyVariant == bodyVariant)
            .OrderByDescending(candidate => GeometryRank(candidate))
            .ThenByDescending(candidate => candidate.Geometry?.TriangleCount ?? candidate.Part.TriangleCount)
            .ThenByDescending(candidate => candidate.Geometry?.VertexCount ?? candidate.Part.VertexCount)
            .ThenByDescending(candidate => candidate.Part.StoredBytes)
            .ThenByDescending(candidate => candidate.Part.Confidence)
            .ThenBy(candidate => candidate.Part.UnitAssetKey.FileId)
            .ThenBy(candidate => candidate.Part.MeshInfoIndex)
            .FirstOrDefault();

    private static IReadOnlySet<UnitMeshPartLayer> ReadSourceLayers(
        IEnumerable<(ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)> attachments)
    {
        var layers = new HashSet<UnitMeshPartLayer>();
        foreach (var layerText in attachments.SelectMany(attachment => attachment.Payload.SourceLayers ?? []))
        {
            if (Enum.TryParse<UnitMeshPartLayer>(layerText, ignoreCase: true, out var layer)
                && layer is not UnitMeshPartLayer.Unknown and not UnitMeshPartLayer.Culling and not UnitMeshPartLayer.Static)
                layers.Add(layer);
        }
        return layers;
    }

    private static (ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)? FindPayload(
        IEnumerable<(ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload)> attachments,
        string bodyVariant)
    {
        foreach (var attachment in attachments)
        {
            if (string.Equals(attachment.Payload.BodyVariant, bodyVariant, StringComparison.OrdinalIgnoreCase))
                return attachment;
        }
        return null;
    }

    private static string DescribeCandidate(DecorationHostCandidate candidate)
        => $"0x{candidate.Part.UnitAssetKey.FileId:x16}/M{candidate.Part.MeshInfoIndex}/{candidate.Part.Layer}/{candidate.Part.BodyVariant}/Q{GeometryRank(candidate)}/{candidate.Geometry?.VertexCount ?? candidate.Part.VertexCount}V/{candidate.Geometry?.TriangleCount ?? candidate.Part.TriangleCount}T/{candidate.Part.StoredBytes}B";

    private static int GeometryRank(DecorationHostCandidate candidate)
        => UnitGeometryRanker.GetRank(candidate.Geometry?.Quality ?? candidate.Part.GeometryQuality);

    private static bool HasRenderableGeometry(DecorationHostCandidate candidate)
        => candidate.Geometry?.HasRenderableGeometry ?? candidate.Part.HasRenderableGeometry;

    private static bool IsRenderableHost(EquipmentUnitPart part)
        => !part.IsCullingMesh
            && part.Layer is not UnitMeshPartLayer.Culling and not UnitMeshPartLayer.Static;

    private static IReadOnlyList<(UnitRawMeshData Raw, UnitMeshInfo Mesh)> ResolveVisibleLodFamily(
        UnitMeshModel model, UnitRawMeshData anchorRaw, UnitMeshInfo anchorMesh)
        => model.RawMeshData
            .Select(raw => (Raw: raw, Mesh: model.Meshes.SingleOrDefault(mesh => mesh.Index == raw.MeshInfoIndex)))
            .Where(item => item.Mesh is not null
                && item.Raw.LodIndex >= 0
                && item.Mesh!.SemanticInfo.IsVisualMesh
                && SameLodFamily(anchorMesh.SemanticInfo, item.Mesh.SemanticInfo))
            .OrderBy(item => item.Raw.LodIndex)
            .ThenBy(item => item.Mesh!.Index)
            .Select(item => (item.Raw, item.Mesh!))
            .ToArray();

    private static bool SameLodFamily(UnitMeshSemanticInfo anchor, UnitMeshSemanticInfo candidate)
    {
        if (!anchor.HasValue || !candidate.HasValue) return true;
        return string.Equals(anchor.Slot, candidate.Slot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(anchor.PieceType, candidate.PieceType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(anchor.BodyType, candidate.BodyType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(anchor.Weight, candidate.Weight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PartMatches(UnitMeshPartKind part, string requested)
        => string.Equals(part.ToString(), requested, StringComparison.OrdinalIgnoreCase)
            || part == UnitMeshPartKind.Pelvis && string.Equals(requested, "Hips", StringComparison.OrdinalIgnoreCase);

    private sealed record DecorationHostCandidate(string ArchiveId, EquipmentUnitPart Part)
    {
        public UnitMeshGeometryFact? Geometry { get; init; }
    }

    private sealed record DecorationResolvedTarget(
        EquipmentUnitPart Part,
        (ModEntity Mod, DecorationAttachmentPlan Plan, DecorationPayloadDocument Payload) Attachment);

    private sealed class StringTupleComparer : IEqualityComparer<(string ModGuid, string BodyVariant)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals((string ModGuid, string BodyVariant) x, (string ModGuid, string BodyVariant) y)
            => string.Equals(x.ModGuid, y.ModGuid, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.BodyVariant, y.BodyVariant, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ModGuid, string BodyVariant) value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.ModGuid), StringComparer.OrdinalIgnoreCase.GetHashCode(value.BodyVariant));
    }

    private static CanonicalDecorationFragment ToCanonical(DecorationMeshFragment fragment)
        => new(fragment.Mesh, fragment.RawMesh, fragment.Stream, fragment.Materials, fragment.BoneInfos, fragment.TransformInfo, fragment.TransformNameHashes);

    private static ulong DecorationNamespace(string id) => Guid.TryParse(id, out var value) ? BitConverter.ToUInt64(value.ToByteArray(), 0) : unchecked((ulong)StringComparer.OrdinalIgnoreCase.GetHashCode(id));

    private static async Task<DecorationPayloadDocument> ReadPayloadAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<DecorationPayloadDocument>(gzip, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Decoration payload is empty.");
    }

    private static string? PrepareGeneratedOverwrite(string directory)
    {
        string? backup = null;
        if (Directory.Exists(directory))
        {
            if (!File.Exists(Path.Combine(directory, MarkerName)))
                throw new InvalidOperationException("主体 Mod 的 Overwrite 目录不是管理器生成，已拒绝覆盖。");
            backup = directory + ".backup-" + Guid.NewGuid().ToString("N");
            Directory.Move(directory, backup);
        }
        Directory.CreateDirectory(directory);
        return backup;
    }

    private static void RemoveGeneratedOverwrite(string directory)
    {
        if (Directory.Exists(directory) && File.Exists(Path.Combine(directory, MarkerName))) Directory.Delete(directory, recursive: true);
    }

    private static IReadOnlyList<string> ResolveOriginalPatchFiles(ModNode host, string modsRoot)
    {
        var directory = Path.Combine(modsRoot, host.RelativePath);
        if (!Directory.Exists(directory)) return [];
        var parser = CoreServices.CreatePatchFileNameParser();
        return Directory.EnumerateFiles(directory)
            .Where(path => parser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base)
            .ToArray();
    }
}
