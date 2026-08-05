using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.PatchWorkspace;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using HD2ModAdaptation.PatchReconstruction.Validation;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using CoreAssetKey = HD2ModCore.Domain.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;

namespace HD2ModCore.Infrastructure;

// Purpose: Plans and executes one same-AssetKey Patch rebuild through the new Canonical/Workspace pipeline.
public sealed class CanonicalSameKeyReconstructionService : IModSameKeyReconstructionService
{
    private readonly IPatchFileNameParser fileNameParser;
    private readonly IAssetArchiveIndexService assetIndex;
    private readonly IArchiveHashesProvider archiveHashes;
    private readonly IPatchWorkspaceReader workspaceReader;
    private readonly PatchUnitMeshReader sourceReader = new();
    private readonly SameKeyCanonicalUnitRebuilder unitRebuilder = new();
    private readonly IPatchWorkspaceWriter workspaceWriter;
    private readonly IPatchValidator patchValidator;

    public CanonicalSameKeyReconstructionService(
        IPatchFileNameParser fileNameParser,
        IAssetArchiveIndexService assetIndex,
        IArchiveHashesProvider archiveHashes,
        IPatchWorkspaceReader? workspaceReader = null,
        IPatchWorkspaceWriter? workspaceWriter = null,
        IPatchValidator? patchValidator = null)
    {
        this.fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
        this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex));
        this.archiveHashes = archiveHashes ?? throw new ArgumentNullException(nameof(archiveHashes));
        this.workspaceReader = workspaceReader ?? new PatchWorkspaceReader();
        this.workspaceWriter = workspaceWriter ?? new PatchWorkspaceWriter();
        this.patchValidator = patchValidator ?? new PatchValidator();
    }

    public async ValueTask<ModSameKeyReconstructionState> InspectAsync(
        ModNode source, string modsRootDirectory, string gameDataDirectory,
        CancellationToken cancellationToken = default, IProgress<OperationProgressEvent>? progress = null, Guid? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var patch = FindBasePatchPaths(source, modsRootDirectory).FirstOrDefault();
        var issues = new List<CoreIssue>();
        if (patch is null) issues.Add(Error("PatchRequired", "Mod 没有 Patch 主文件。", source.Id));
        if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory)) issues.Add(Error("GameDataMissing", "Game Data 不可用。", source.Id));
        var current = false;
        if (issues.Count == 0)
        {
            var status = await assetIndex.GetIndexStatusAsync(gameDataDirectory, await archiveHashes.GetArchiveHashesJsonAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            current = status.IsCurrent;
            if (!current) issues.Add(Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期。", source.Id));
        }
        var plan = issues.Count == 0 ? await PlanAsync(patch!, gameDataDirectory, cancellationToken, progress, source.Id).ConfigureAwait(false) : null;
        if (plan is not null) issues.AddRange(plan.Issues);
        return new ModSameKeyReconstructionState(source.Id, patch, plan, current,
            plan?.Units.Count(unit => unit.Adaptation?.ReplacementCount > 0) ?? 0,
            plan?.Units.Count(unit => unit.Adaptation?.ReplacementCount == 0) ?? 0,
            plan?.Units.Sum(unit => unit.Adaptation?.ReplacementCount ?? 0) ?? 0,
            plan?.Units.Sum(unit => unit.Adaptation?.MinifiedCount ?? 0) ?? 0,
            0, issues);
    }

    public async ValueTask<SameKeyReconstructionOperationResult> GenerateCandidateAsync(
        ModNode source, string modsRootDirectory, string gameDataDirectory, string outputRootDirectory,
        CancellationToken cancellationToken = default, IProgress<OperationProgressEvent>? progress = null, Guid? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Report(progress, operationId, "InspectEligibility", "正在检查同 ID Canonical 重建资格", 0, 1);
        var patch = FindBasePatchPaths(source, modsRootDirectory).FirstOrDefault();
        if (patch is null) return Failure([Error("PatchRequired", "Mod 没有 Patch 主文件。", source.Id)]);
        if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory)) return Failure([Error("GameDataMissing", "Game Data 不可用。", source.Id)]);
        var status = await assetIndex.GetIndexStatusAsync(gameDataDirectory, await archiveHashes.GetArchiveHashesJsonAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        if (!status.IsCurrent) return Failure([Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期。", source.Id)]);

        Report(progress, operationId, "Plan", "正在生成同 ID Canonical 重建计划", 0, 1);
        var index = await workspaceReader.ReadIndexAsync(patch, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAsync(patch, gameDataDirectory, cancellationToken, progress, source.Id).ConfigureAwait(false);
        var issues = plan.Issues.ToList();
        if (issues.Any(issue => issue.Severity == CoreIssueSeverity.Error)) return Failure(issues);
        var resolver = new AdaptationGameDataPackageResolver(gameDataDirectory);
        var targetReader = new GameDataUnitMeshReader(resolver);
        var jobs = new List<PatchWorkspaceJobResult>();
        var removed = index.Entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId || entry.AssetKey.TypeId == PatchUnitMeshReader.CompositeUnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
        var sourceEntries = index.Entries;
        Report(progress, operationId, "BuildCandidate", "正在执行 Canonical Unit 作业", 0, plan.Units.Count);
        var completedUnits = 0;
        foreach (var unitPlan in plan.Units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceEntry = sourceEntries.Single(entry => entry.AssetKey == new AdaptationAssetKey(unitPlan.UnitAssetKey.TypeId, unitPlan.UnitAssetKey.FileId));
            var sourceUnit = await sourceReader.ReadAsync(sourceEntry, sourceEntries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
            var targetArchive = unitPlan.TargetArchive!.ArchiveId;
            var targetUnit = await targetReader.ReadAsync(targetArchive, sourceEntry.AssetKey, allowGlobalDependencySearch: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            var mappings = unitPlan.Adaptation!.Steps.Where(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource && step.SourceMeshInfoIndex is not null)
                .Select(step => new TargetShellMeshMapping(sourceEntry.AssetKey, step.SourceMeshInfoIndex!.Value, step.TargetMeshInfoIndex)).ToArray();
            var rebuilt = unitRebuilder.Rebuild(new SameKeyCanonicalUnitRebuildRequest(sourceUnit, targetUnit, mappings));
            if (!rebuilt.IsValid || rebuilt.Job is null)
                issues.AddRange(rebuilt.Diagnostics.Select(diagnostic => Error(diagnostic.Code, diagnostic.Message, source.Id)));
            else jobs.Add(rebuilt.Job);
            Report(progress, operationId, "BuildCandidate", $"已完成 Unit {unitPlan.UnitAssetKey.FileId:x16}", ++completedUnits, plan.Units.Count);
        }
        if (issues.Any(issue => issue.Severity == CoreIssueSeverity.Error)) return Failure(issues);
        var output = Path.GetFullPath(outputRootDirectory);
        Directory.CreateDirectory(output);
        Report(progress, operationId, "WriteCandidate", "正在打包 Canonical Patch", 0, 1);
        var write = await workspaceWriter.WriteAsync(index, jobs, removed, output, Path.GetFileName(patch),
            headerTemplateTocData: (await resolver.GetPackageTocAsync(plan.Units.First().TargetArchive!.ArchiveId, cancellationToken).ConfigureAwait(false))?.Data,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Report(progress, operationId, "ValidateCandidate", "正在回读验证 Canonical Patch", 0, 1);
        var expectedUnitVersion = jobs.SelectMany(job => job.Outputs)
            .Where(entry => entry.Key.TypeId == PatchUnitMeshReader.UnitTypeId)
            .Select(entry => BitConverter.ToUInt32(entry.TocData!, 0x2c))
            .Distinct()
            .Take(2)
            .ToArray();
        var validation = await patchValidator.ValidateAsync(write.TocFilePath,
            new PatchValidationOptions(
                RequirePatchLocalComposite: false,
                ReportEmptyUnitGeometry: true,
                ExpectedUnitVersion: expectedUnitVersion.Length == 1 ? expectedUnitVersion[0] : null,
                TreatOutdatedUnitVersionAsError: true,
                SourcePatchTocFilePath: patch,
                RequireSourceGeometryPreservation: true,
                RequireFiniteVisiblePositions: true,
                RequireBoundVisibleMaterialSlots: true),
            cancellationToken).ConfigureAwait(false);
        issues.AddRange(validation.Issues.Select(issue => new CoreIssue(
            issue.Severity == PatchValidationSeverity.Error ? CoreIssueSeverity.Error : CoreIssueSeverity.Warning,
            $"PatchValidation.{issue.Code}", issue.Message, NodeId: source.Id)));
        if (!validation.IsValid)
        {
            Report(progress, operationId, "ValidateCandidate", "Canonical Patch 验证失败", 1, 1);
            return Failure(issues);
        }
        Report(progress, operationId, "ValidateCandidate", validation.HasWarnings ? "Canonical Patch 验证完成（存在警告）" : "Canonical Patch 验证完成", 1, 1);
        return new SameKeyReconstructionOperationResult(true, output, null, null, jobs.Count,
            plan.Units.Count(unit => unit.Adaptation?.ReplacementCount > 0), plan.Units.Count(unit => unit.Adaptation?.ReplacementCount == 0),
            jobs.Sum(job => job.Outputs.Count), plan.Units.Sum(unit => unit.Adaptation?.MinifiedCount ?? 0), issues);
    }

    private async ValueTask<SameKeyReconstructionPlan> PlanAsync(string patch, string gameData, CancellationToken cancellationToken, IProgress<OperationProgressEvent>? progress, ModNodeId nodeId)
    {
        var index = await workspaceReader.ReadIndexAsync(patch, cancellationToken).ConfigureAwait(false);
        var units = index.Entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).ToArray();
        var matches = await assetIndex.FindAssetArchivesAsync(units.Select(entry => new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId)).ToHashSet(), cancellationToken).ConfigureAwait(false);
        var byKey = matches.ToDictionary(match => match.AssetKey);
        var plans = new List<SameKeyUnitReconstructionPlan>();
        var targetReader = new GameDataUnitMeshReader(new AdaptationGameDataPackageResolver(gameData));
        var sourceEntries = index.Entries;
        foreach (var entry in units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId);
            var issues = new List<CoreIssue>();
            if (!byKey.TryGetValue(key, out var match) || match.Archives.Count == 0)
            {
                plans.Add(new SameKeyUnitReconstructionPlan(key, ToCoreEntry(entry), null, [], null, [Error("CurrentTargetMissing", "找不到同 ID current target Unit。", nodeId)]));
                continue;
            }
            var archive = match.Archives.OrderBy(item => item.CategoryOrder).ThenBy(item => item.ArchiveOrder).First();
            try
            {
                var sourceUnit = await sourceReader.ReadAsync(entry, sourceEntries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
                var targetUnit = await targetReader.ReadAsync(archive.ArchiveId, entry.AssetKey, allowGlobalDependencySearch: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                var mappings = BuildMappings(entry.AssetKey, sourceUnit.Model, targetUnit.Model);
                var steps = mappings.Select(mapping => new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.ReplaceWithSource, mapping.TargetMeshInfoIndex, mapping.SourceMeshInfoIndex, "Same AssetKey and MeshInfoIndex mapping.", null)).ToList();
                steps.AddRange(targetUnit.Model.RawMeshData.Where(raw => steps.All(step => step.TargetMeshInfoIndex != raw.MeshInfoIndex)).Select(raw => new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.MinifyTarget, raw.MeshInfoIndex, null, "Target mesh has no source model mapping.", null)));
                var adaptation = new UnitMeshAdaptationPlan(new UnitMeshAdaptationIntent(ToCoreEntry(entry), archive.ArchiveId, null), true, [], steps, "New Canonical same-key MeshInfo plan.");
                plans.Add(new SameKeyUnitReconstructionPlan(key, ToCoreEntry(entry), archive, match.Archives, adaptation, issues, TargetMeshCount: targetUnit.Model.RawMeshData.Count, CoveredTargetMeshCount: targetUnit.Model.RawMeshData.Count));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or KeyNotFoundException)
            {
                issues.Add(Error("UnitPlanningFailed", exception.Message, nodeId));
                plans.Add(new SameKeyUnitReconstructionPlan(key, ToCoreEntry(entry), archive, match.Archives, null, issues));
            }
            progress?.Report(new OperationProgressEvent(Guid.NewGuid(), null, OperationKind.PatchRepair, OperationStage.Processing, OperationState.Progress, plans.Count, units.Length, $"已规划 Unit {plans.Count}/{units.Length}", null, DateTimeOffset.UtcNow, plans.Count, "Plan", "正在生成同 ID Canonical 计划"));
        }
        return new SameKeyReconstructionPlan(new SameKeyReconstructionRequest(patch, gameData), plans, []);
    }

    private static IReadOnlyList<TargetShellMeshMapping> BuildMappings(AdaptationAssetKey sourceKey, UnitMeshModel source, UnitMeshModel target)
        => target.RawMeshData.Where(targetRaw => source.RawMeshData.Any(sourceRaw => sourceRaw.MeshInfoIndex == targetRaw.MeshInfoIndex && sourceRaw.Vertices.Count > 0 && sourceRaw.Triangles.Count > 0))
            .Select(targetRaw => new TargetShellMeshMapping(sourceKey, targetRaw.MeshInfoIndex, targetRaw.MeshInfoIndex)).ToArray();

    private IReadOnlyList<string> FindBasePatchPaths(ModNode node, string root)
        => Directory.Exists(Path.Combine(root, node.RelativePath))
            ? Directory.EnumerateFiles(Path.Combine(root, node.RelativePath), "*", SearchOption.TopDirectoryOnly).Where(path => fileNameParser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base).OrderBy(path => path).ToArray()
            : Array.Empty<string>();

    private static HD2ModCore.Domain.PatchTocEntry ToCoreEntry(HD2ModAdaptation.PatchReconstruction.PatchTocEntry entry) => new(new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), entry.SourceFilePath, entry.SourceFileName, entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset, entry.Unknown1, entry.Unknown2, entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.Unknown3, entry.Unknown4, entry.EntryIndex);
    private static CoreIssue Error(string code, string message, ModNodeId nodeId) => new(CoreIssueSeverity.Error, code, message, NodeId: nodeId);
    private static SameKeyReconstructionOperationResult Failure(IReadOnlyList<CoreIssue> issues) => new(false, null, null, null, 0, 0, 0, 0, 0, issues);

    private static void Report(IProgress<OperationProgressEvent>? progress, Guid? operationId, string stageId, string message, long completed, long total)
        => progress?.Report(new OperationProgressEvent(
            operationId.GetValueOrDefault(Guid.NewGuid()), null, OperationKind.PatchRepair, OperationStage.Processing,
            OperationState.Progress, completed, total, message, null, DateTimeOffset.UtcNow, completed, stageId, message));
}
