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
        var parts = catalog.SelectMany(entry => entry.Parts).GroupBy(part => (part.UnitAssetKey, part.MeshInfoIndex)).ToDictionary(group => group.Key, group => group.First());
        var edits = new List<PatchUnitMeshEditResult>();
        var reader = new PatchUnitMeshReader();
        foreach (var entry in entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId))
        {
            var targetParts = parts.Where(pair => pair.Key.UnitAssetKey.TypeId == entry.AssetKey.TypeId && pair.Key.UnitAssetKey.FileId == entry.AssetKey.FileId).Select(pair => pair.Value).ToArray();
            if (targetParts.Length == 0) continue;
            var matching = attachments.Where(attachment => targetParts.Any(part => Matches(attachment.Plan, attachment.Payload, part))).ToArray();
            if (matching.Length == 0) continue;
            // Multiple fragments/attachments targeting one physical Unit need one combined palette.
            // Refuse this case until the batch appender is introduced rather than serializing a
            // second pass from an already generated Unit.
            if (matching.Length != 1) throw new InvalidDataException($"Unit 0x{entry.AssetKey.FileId:x16} has multiple decoration attachments; batch append is not available yet.");
            var attachment = matching[0];
            var targetPart = targetParts.Single(part => Matches(attachment.Plan, attachment.Payload, part));
            var unit = await reader.ReadAsync(entry, entries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
            var targetRaw = unit.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == targetPart.MeshInfoIndex)
                ?? throw new InvalidDataException($"Target MeshInfo {targetPart.MeshInfoIndex} has no readable RawMesh.");
            var fragments = attachment.Payload.Fragments.Where(item => item.RawMesh.LodIndex == targetRaw.LodIndex).ToArray();
            if (fragments.Length != 1)
            {
                var available = string.Join(",", attachment.Payload.Fragments.Select(item => item.RawMesh.LodIndex).Distinct().OrderBy(value => value));
                throw new InvalidDataException($"装饰 payload 没有与目标 LOD {targetRaw.LodIndex} 一一对应的来源 Mesh（可用 LOD：{available}）。");
            }
            var fragment = fragments[0];
            LogService.Info($"装饰合并 Unit：Patch={Path.GetFileName(patch)}，目标=0x{entry.AssetKey.FileId:x16}，Mesh={targetPart.MeshInfoIndex}，目标LOD={targetRaw.LodIndex}，部位={targetPart.PartKind}，身形={targetPart.BodyVariant}，来源装饰={attachment.Mod.Guid}，来源LOD={fragment.RawMesh.LodIndex}，来源顶点={fragment.RawMesh.Vertices.Count}，来源三角={fragment.RawMesh.Triangles.Count}");
            var result = new CanonicalDecorationUnitAppender().TryAppend(unit, targetPart.MeshInfoIndex, ToCanonical(fragment), DecorationNamespace(attachment.Mod.Guid), avatar);
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
            LogService.Info($"装饰合并写出 Patch：源={patch}，Unit 编辑数={edits}");
            await new PatchArchiveWriter().WriteAsync(patch, output, edits, overwriteExisting: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return edits.Count;
    }

    private static bool Matches(DecorationAttachmentPlan plan, DecorationPayloadDocument payload, EquipmentUnitPart part)
        => !part.IsCullingMesh
            && PartMatches(part.PartKind, plan.TargetPart)
            && string.Equals(part.BodyVariant.ToString(), payload.BodyVariant, StringComparison.OrdinalIgnoreCase);

    private static bool PartMatches(UnitMeshPartKind part, string requested)
        => string.Equals(part.ToString(), requested, StringComparison.OrdinalIgnoreCase)
            || part == UnitMeshPartKind.Pelvis && string.Equals(requested, "Hips", StringComparison.OrdinalIgnoreCase);

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
