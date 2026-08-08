using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.PatchWorkspace;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using CoreAssetKey = HD2ModCore.Domain.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using System.Collections.Concurrent;

namespace HD2ModCore.Infrastructure;

// Purpose: Plans and executes one same-AssetKey Patch rebuild through the new Canonical/Workspace pipeline.
public sealed class CanonicalSameKeyReconstructionService : IModSameKeyReconstructionService
{
    private static readonly ConcurrentDictionary<Guid, long> progressSequences = new();
    private readonly IPatchFileNameParser fileNameParser;
    private readonly IAssetArchiveIndexService assetIndex;
    private readonly IArchiveHashesProvider archiveHashes;
    private readonly IAdvancedModAnalysisService advancedAnalysis;
    private readonly ISourceUnitEligibilityService sourceUnitEligibility;
    private readonly IPatchWorkspaceReader workspaceReader;
    private readonly PatchUnitMeshReader sourceReader = new();
    private readonly SameKeyCanonicalUnitRebuilder unitRebuilder = new();
    private readonly IPatchWorkspaceWriter workspaceWriter;
    private readonly IPatchOperationWorkspaceFactory operationWorkspaceFactory;
    private readonly ICanonicalHiddenUnitOutputCache hiddenUnitCache;
    private readonly CanonicalHiddenUnitBuilder hiddenUnitBuilder;

    public CanonicalSameKeyReconstructionService(
        IPatchFileNameParser fileNameParser,
        IAssetArchiveIndexService assetIndex,
        IArchiveHashesProvider archiveHashes,
        IAdvancedModAnalysisService advancedAnalysis,
        ISourceUnitEligibilityService sourceUnitEligibility,
        IPatchWorkspaceReader? workspaceReader = null,
        IPatchWorkspaceWriter? workspaceWriter = null,
        IPatchOperationWorkspaceFactory? operationWorkspaceFactory = null,
        ICanonicalHiddenUnitOutputCache? hiddenUnitCache = null,
        CanonicalHiddenUnitBuilder? hiddenUnitBuilder = null)
    {
        this.fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
        this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex));
        this.archiveHashes = archiveHashes ?? throw new ArgumentNullException(nameof(archiveHashes));
        this.advancedAnalysis = advancedAnalysis ?? throw new ArgumentNullException(nameof(advancedAnalysis));
        this.sourceUnitEligibility = sourceUnitEligibility ?? throw new ArgumentNullException(nameof(sourceUnitEligibility));
        this.workspaceReader = workspaceReader ?? new PatchWorkspaceReader();
        this.workspaceWriter = workspaceWriter ?? new PatchWorkspaceWriter();
        this.operationWorkspaceFactory = operationWorkspaceFactory ?? new PatchOperationWorkspaceFactory();
        this.hiddenUnitCache = hiddenUnitCache ?? new CanonicalHiddenUnitOutputCache();
        this.hiddenUnitBuilder = hiddenUnitBuilder ?? new CanonicalHiddenUnitBuilder();
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
        var plan = issues.Count == 0 ? await PlanAsync(source, modsRootDirectory, patch!, gameDataDirectory, cancellationToken, progress, operationId).ConfigureAwait(false) : null;
        if (plan is not null) issues.AddRange(plan.Issues);
        return new ModSameKeyReconstructionState(source.Id, patch, plan, current,
            plan?.Units.Count(unit => unit.IsGeometryEligible) ?? 0,
            plan?.Units.Count(unit => !unit.IsGeometryEligible) ?? 0,
            0,
            0,
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
        await hiddenUnitCache.InitializeAsync(status.CurrentSourceFingerprint, status.IsCurrent, cancellationToken).ConfigureAwait(false);
        if (!status.IsCurrent) return Failure([Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期。", source.Id)]);

        Report(progress, operationId, "Plan", "正在生成同 ID Canonical 重建计划", 0, 1);
        var planStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var index = await workspaceReader.ReadIndexAsync(patch, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAsync(source, modsRootDirectory, patch, gameDataDirectory, cancellationToken, progress, operationId, index).ConfigureAwait(false);
        Report(progress, operationId, "Plan", $"Canonical 重建计划完成，用时={planStopwatch.ElapsedMilliseconds}ms", 1, 1);
        var issues = plan.Issues.ToList();
        if (issues.Any(issue => issue.Severity == CoreIssueSeverity.Error)) return Failure(issues);
        var resolver = new AdaptationGameDataPackageResolver(gameDataDirectory);
        var targetReader = new GameDataUnitMeshReader(resolver);
        Report(progress, operationId, "PrepareAvatarRig", "正在读取 Canonical Avatar Rig 变换", 0, 1);
        var avatarRigStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var avatarTransforms = await new CanonicalAvatarRigReader(resolver)
            .ReadTransformInfoAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        Report(progress, operationId, "PrepareAvatarRig", $"Canonical Avatar Rig 变换读取完成，用时={avatarRigStopwatch.ElapsedMilliseconds}ms", 1, 1);
        var output = Path.GetFullPath(outputRootDirectory);
        Directory.CreateDirectory(output);
        using var operationWorkspace = operationWorkspaceFactory.Create(output, "same-key-reconstruction");
        var jobsBySequence = new PatchWorkspaceJobResult?[plan.Units.Count];
        var removed = index.Entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId || entry.AssetKey.TypeId == PatchUnitMeshReader.CompositeUnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
        var sourceEntries = index.Entries;
        Report(progress, operationId, "BuildCandidate", "正在执行 Canonical Unit 作业", 0, plan.Units.Count);
        var sourceReadElapsed = TimeSpan.Zero;
        var targetReadElapsed = TimeSpan.Zero;
        var mappingElapsed = TimeSpan.Zero;
        var rebuildElapsed = TimeSpan.Zero;
        var stagingElapsed = TimeSpan.Zero;
		var rebuildTelemetry = new CanonicalUnitRebuildTelemetryAccumulator();
        var completedUnits = 0;
        var resultGate = new object();
        var replacementUnitCount = 0;
        var minifyOnlyUnitCount = 0;
        var replacementMeshCount = 0;
        var minifiedMeshCount = 0;
        using var positionDiagnostics = CanonicalPositionDiagnostics.Suppress();
        var unitJobs = plan.Units.Select((unit, index) =>
            (Sequence: index, UnitKey: $"0x{unit.UnitAssetKey.FileId:x16}")).ToArray();
        var results = await UnitJobExecutor.ExecuteAsync(
            unitJobs,
            async (jobIndex, token) =>
            {
                var unitPlan = plan.Units[jobIndex];
                var sourceEntry = sourceEntries.Single(entry => entry.AssetKey == new AdaptationAssetKey(unitPlan.UnitAssetKey.TypeId, unitPlan.UnitAssetKey.FileId));
                var archiveId = unitPlan.TargetArchive!.ArchiveId;
                var sourceIsEligible = plan.EligibleSourceUnitAssetKeys?.Contains(unitPlan.UnitAssetKey) == true;
                if (!sourceIsEligible)
                {
                    var cached = await hiddenUnitCache.TryReadAsync(archiveId, sourceEntry.AssetKey, token).ConfigureAwait(false);
                    if (cached is not null)
                    {
                        var cachedResult = new SameKeyCanonicalUnitRebuildResult(PatchWorkspaceJobResult.Unit(cached.Entry), 0, cached.HiddenMeshCount, []);
                        return new SameKeyUnitJobResult(jobIndex, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, cachedResult);
                    }
                }
                var unitStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var targetUnit = await targetReader.ReadAsync(
                    archiveId,
                    sourceEntry.AssetKey,
                    allowGlobalDependencySearch: true,
                    cancellationToken: token).ConfigureAwait(false);
                var localTargetRead = unitStopwatch.Elapsed;
                unitStopwatch.Restart();
                if (!sourceIsEligible)
                {
                    var hidden = hiddenUnitBuilder.Build(targetUnit, avatarTransforms);
                    await hiddenUnitCache.StoreAsync(archiveId, hidden, token).ConfigureAwait(false);
                    var hiddenResult = new SameKeyCanonicalUnitRebuildResult(PatchWorkspaceJobResult.Unit(hidden.Entry), 0, hidden.HiddenMeshCount, []);
                    return new SameKeyUnitJobResult(jobIndex, TimeSpan.Zero, localTargetRead, TimeSpan.Zero, unitStopwatch.Elapsed, hiddenResult);
                }
                var localSourceReader = new PatchUnitMeshReader();
                var sourceUnit = await localSourceReader.ReadAsync(sourceEntry, sourceEntries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, token).ConfigureAwait(false);
                var localSourceRead = unitStopwatch.Elapsed;
                unitStopwatch.Restart();
                var mappings = BuildMappings(sourceEntry.AssetKey, sourceUnit.Model, targetUnit.Model,
                    sourceIsEligible).ToArray();
                var localMapping = unitStopwatch.Elapsed;
                unitStopwatch.Restart();
                SameKeyCanonicalUnitRebuildResult rebuilt;
                if (mappings.Length == 0)
                {
                    var hidden = hiddenUnitBuilder.Build(targetUnit, avatarTransforms);
                    await hiddenUnitCache.StoreAsync(archiveId, hidden, token).ConfigureAwait(false);
                    rebuilt = new SameKeyCanonicalUnitRebuildResult(PatchWorkspaceJobResult.Unit(hidden.Entry), 0, hidden.HiddenMeshCount, []);
                }
                else
                {
                    rebuilt = new SameKeyCanonicalUnitRebuilder().Rebuild(new SameKeyCanonicalUnitRebuildRequest(sourceUnit, targetUnit, mappings)
                    {
                        AvatarTransformInfo = avatarTransforms
                    });
                }
                return new SameKeyUnitJobResult(jobIndex, localSourceRead, localTargetRead, localMapping,
                    unitStopwatch.Elapsed, rebuilt);
            },
	            (sequence, result) =>
	            {
	                var unitPlan = plan.Units[sequence];
	                lock (resultGate)
	                {
	                    sourceReadElapsed += result.SourceRead;
	                    targetReadElapsed += result.TargetRead;
	                    mappingElapsed += result.Mapping;
	                    rebuildElapsed += result.RebuildElapsed;
	                    var rebuilt = result.Rebuild;
	                    if (!rebuilt.IsValid || rebuilt.Job is null)
	                        issues.AddRange(rebuilt.Diagnostics.Select(diagnostic => Error(diagnostic.Code, $"Unit=0x{unitPlan.UnitAssetKey.FileId:x16}; {diagnostic.Message}", source.Id)));
	                    else
	                    {
	                        rebuildTelemetry.Add(rebuilt.Telemetry);
	                        var stagingStopwatch = System.Diagnostics.Stopwatch.StartNew();
	                        jobsBySequence[sequence] = operationWorkspace.Stage(rebuilt.Job);
	                        stagingElapsed += stagingStopwatch.Elapsed;
	                        replacementMeshCount += rebuilt.ReplacedMeshCount;
	                        minifiedMeshCount += rebuilt.HiddenMeshCount;
	                        if (rebuilt.ReplacedMeshCount > 0) replacementUnitCount++;
	                        else minifyOnlyUnitCount++;
	                    }
	                }
	                foreach (var observation in result.Rebuild.MaterialObservations ?? [])
	                    Report(progress, operationId, "MaterialBindingDiagnostics", observation.Message, completedUnits, plan.Units.Count);
	                Report(progress, operationId, "BuildCandidate", $"宸插畬鎴?Unit {unitPlan.UnitAssetKey.FileId:x16}", Interlocked.Increment(ref completedUnits), plan.Units.Count);
	                return ValueTask.CompletedTask;
	            },
	            cancellationToken: cancellationToken).ConfigureAwait(false);

        var jobs = jobsBySequence.Where(job => job is not null).Select(job => job!).ToList();

        if (results.Any(result => result is not null)) foreach (var result in results.OrderBy(result => result.Sequence))
        {
            sourceReadElapsed += result.SourceRead;
            targetReadElapsed += result.TargetRead;
            mappingElapsed += result.Mapping;
            rebuildElapsed += result.RebuildElapsed;
            var unitPlan = plan.Units[result.Sequence];
            cancellationToken.ThrowIfCancellationRequested();
            var rebuilt = result.Rebuild;
            if (!rebuilt.IsValid || rebuilt.Job is null)
                issues.AddRange(rebuilt.Diagnostics.Select(diagnostic => Error(
                    diagnostic.Code,
                    $"Unit=0x{unitPlan.UnitAssetKey.FileId:x16}; {diagnostic.Message}",
                    source.Id)));
            else
            {
				rebuildTelemetry.Add(rebuilt.Telemetry);
                var stagingStopwatch = System.Diagnostics.Stopwatch.StartNew();
                jobs.Add(operationWorkspace.Stage(rebuilt.Job));
                stagingElapsed += stagingStopwatch.Elapsed;
                foreach (var observation in rebuilt.MaterialObservations ?? [])
                    Report(progress, operationId, "MaterialBindingDiagnostics", observation.Message, completedUnits, plan.Units.Count);

                replacementMeshCount += rebuilt.ReplacedMeshCount;
                minifiedMeshCount += rebuilt.HiddenMeshCount;
                if (rebuilt.ReplacedMeshCount > 0) replacementUnitCount++;
                else minifyOnlyUnitCount++;
            }
            Report(progress, operationId, "BuildCandidate", $"已完成 Unit {unitPlan.UnitAssetKey.FileId:x16}", ++completedUnits, plan.Units.Count);
        }
        Report(progress, operationId, "BuildCandidateMetrics", $"Same-key Unit 作业耗时：来源读取={sourceReadElapsed.TotalMilliseconds:F0}ms，目标读取={targetReadElapsed.TotalMilliseconds:F0}ms，重建={rebuildElapsed.TotalMilliseconds:F0}ms，落盘={stagingElapsed.TotalMilliseconds:F0}ms", completedUnits, plan.Units.Count);
        if (issues.Any(issue => issue.Severity == CoreIssueSeverity.Error)) return Failure(issues);
        Report(progress, operationId, "WriteCandidate", "正在打包 Canonical Patch", 0, 1);
        Report(progress, operationId, "CanonicalUnitJobMetrics", $"Canonical Unit job metrics: Flow=SameKey, SourceRead={sourceReadElapsed.TotalMilliseconds:F0}ms, TargetRead={targetReadElapsed.TotalMilliseconds:F0}ms, Mapping={mappingElapsed.TotalMilliseconds:F0}ms, Rebuild={rebuildElapsed.TotalMilliseconds:F0}ms, Staging={stagingElapsed.TotalMilliseconds:F0}ms, {rebuildTelemetry.Snapshot().Describe()}", completedUnits, plan.Units.Count);
        var writeStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var write = await workspaceWriter.WriteAsync(index, jobs, removed, output, Path.GetFileName(patch),
            headerTemplateTocData: (await resolver.GetPackageTocAsync(plan.Units.First().TargetArchive!.ArchiveId, cancellationToken).ConfigureAwait(false))?.Data,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Report(progress, operationId, "WriteCandidate", $"Canonical Patch 打包完成，用时={writeStopwatch.ElapsedMilliseconds}ms", 1, 1);
        return new SameKeyReconstructionOperationResult(true, output, null, null, jobs.Count,
            replacementUnitCount, minifyOnlyUnitCount, replacementMeshCount, minifiedMeshCount, issues);
    }

    private async ValueTask<SameKeyReconstructionPlan> PlanAsync(
        ModNode source,
        string modsRootDirectory,
        string patch,
        string gameData,
        CancellationToken cancellationToken,
        IProgress<OperationProgressEvent>? progress,
        Guid? operationId,
        PatchWorkspaceIndex? knownIndex = null)
    {
        var analyses = await advancedAnalysis.GetRequiredAnalysesAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
        var sourceEligibility = sourceUnitEligibility.Select(analyses);
        var nodeId = source.Id;
        var analysis = analyses.LastOrDefault(candidate => string.Equals(
            Path.GetFullPath(candidate.Input.PatchTocFilePath),
            Path.GetFullPath(patch),
            StringComparison.OrdinalIgnoreCase));
        var index = knownIndex;
        var entries = index?.Entries ?? analysis?.Entries;
        if (entries is null || entries.Count == 0)
        {
            // Cache entries may be unavailable for old cache versions; TOC metadata is
            // still cheap to read and does not decode Unit payloads.
            index = await workspaceReader.ReadIndexAsync(patch, cancellationToken).ConfigureAwait(false);
            entries = index.Entries;
        }
        var units = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).ToArray();
        var matches = await assetIndex.FindAssetArchivesAsync(units.Select(entry => new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId)).ToHashSet(), cancellationToken).ConfigureAwait(false);
        var byKey = matches.ToDictionary(match => match.AssetKey);
        var plans = new List<SameKeyUnitReconstructionPlan>();
        foreach (var entry in units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId);
            var issues = new List<CoreIssue>();
            if (!byKey.TryGetValue(key, out var match) || match.Archives.Count == 0)
            {
                plans.Add(new SameKeyUnitReconstructionPlan(key, ToCoreEntry(entry), null, [], null, [Error("CurrentTargetMissing", "找不到同 ID current target Unit。", nodeId)], IsSourceGeometryEligible: sourceEligibility.EligibleUnitAssetKeys.Contains(key)));
                continue;
            }
            var archive = match.Archives.OrderBy(item => item.CategoryOrder).ThenBy(item => item.ArchiveOrder).First();
            plans.Add(new SameKeyUnitReconstructionPlan(
                key,
                ToCoreEntry(entry),
                archive,
                match.Archives,
                null,
                issues,
                IsSourceGeometryEligible: sourceEligibility.EligibleUnitAssetKeys.Contains(key)));
            Report(progress, operationId, "Plan", $"已规划 Unit {plans.Count}/{units.Length}", plans.Count, units.Length);
        }
        return new SameKeyReconstructionPlan(new SameKeyReconstructionRequest(patch, gameData), plans, [], sourceEligibility.EligibleUnitAssetKeys);
    }

    private sealed record SameKeyUnitJobResult(
        int Sequence,
        TimeSpan SourceRead,
        TimeSpan TargetRead,
        TimeSpan Mapping,
        TimeSpan RebuildElapsed,
        SameKeyCanonicalUnitRebuildResult Rebuild);

    private static IReadOnlyList<TargetShellMeshMapping> BuildMappings(AdaptationAssetKey sourceKey, UnitMeshModel source, UnitMeshModel target, bool isEligibleSourceUnit)
    {
        if (!isEligibleSourceUnit)
            return Array.Empty<TargetShellMeshMapping>();

        var sourceLod0 = source.RawMeshData
            .Where(raw => raw.LodIndex == 0 && raw.Triangles.Count > 1 && raw.Vertices.Count > 3)
            .OrderByDescending(raw => raw.Triangles.Count)
            .ThenByDescending(raw => raw.Vertices.Count)
            .FirstOrDefault();
        var targetLod0 = target.RawMeshData
            .Where(raw => raw.LodIndex == 0 && raw.Triangles.Count > 1 && raw.Vertices.Count > 3)
            .OrderByDescending(raw => raw.Triangles.Count)
            .ThenByDescending(raw => raw.Vertices.Count)
            .FirstOrDefault();
        if (sourceLod0 is null || targetLod0 is null)
            return Array.Empty<TargetShellMeshMapping>();

        var expanded = CanonicalAutoLodMappingExpander.Expand(
            target,
            new Dictionary<AdaptationAssetKey, UnitMeshModel> { [sourceKey] = source },
            [new CanonicalReplacementMapping(
                new CanonicalMeshKey(sourceKey, sourceLod0.MeshInfoIndex),
                new CanonicalMeshKey(sourceKey, targetLod0.MeshInfoIndex),
                SkinningMode: CanonicalSkinningMode.BindStaticToTargetMeshTransform,
                BoneAnchor: CanonicalBoneAnchor.TargetMeshTransform)]);
        return expanded
            .Select(mapping => new TargetShellMeshMapping(sourceKey, mapping.Source.MeshInfoIndex, mapping.Target.MeshInfoIndex))
            .ToArray();
    }

    private IReadOnlyList<string> FindBasePatchPaths(ModNode node, string root)
        => Directory.Exists(Path.Combine(root, node.RelativePath))
            ? Directory.EnumerateFiles(Path.Combine(root, node.RelativePath), "*", SearchOption.TopDirectoryOnly).Where(path => fileNameParser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base).OrderBy(path => path).ToArray()
            : Array.Empty<string>();

    private static HD2ModCore.Domain.PatchTocEntry ToCoreEntry(HD2ModAdaptation.PatchReconstruction.PatchTocEntry entry) => new(new CoreAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), entry.SourceFilePath, entry.SourceFileName, entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset, entry.Unknown1, entry.Unknown2, entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.Unknown3, entry.Unknown4, entry.EntryIndex);
    private static CoreIssue Error(string code, string message, ModNodeId nodeId) => new(CoreIssueSeverity.Error, code, message, NodeId: nodeId);
    private static SameKeyReconstructionOperationResult Failure(IReadOnlyList<CoreIssue> issues) => new(false, null, null, null, 0, 0, 0, 0, 0, issues);

    private static void Report(IProgress<OperationProgressEvent>? progress, Guid? operationId, string stageId, string message, long completed, long total)
    {
        if (progress is null) return;
        var id = operationId.GetValueOrDefault(Guid.NewGuid());
        var sequence = progressSequences.AddOrUpdate(id, 1, static (_, previous) => previous + 1);
        progress.Report(new OperationProgressEvent(
            id, null, OperationKind.PatchRepair, OperationStage.Processing,
            OperationState.Progress, completed, total, message, null, DateTimeOffset.UtcNow, sequence, stageId, message));
    }
}
