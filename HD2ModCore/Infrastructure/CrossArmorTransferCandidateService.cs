using System.Text.Json;
using System.Diagnostics;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using AdaptationGameDataUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMeshReader;
using AdaptationMaterialDependencyResolver = HD2ModAdaptation.PatchReconstruction.MaterialDependencyResolver;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationPatchUnitMesh = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMesh;
using AdaptationPatchUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMeshReader;
using AdaptationCrossArmorTargetShellPatchOperation = HD2ModAdaptation.PatchReconstruction.UnitMesh.CrossArmorTargetShellPatchOperation;
using AdaptationCrossArmorTargetShellPatchOperationRequest = HD2ModAdaptation.PatchReconstruction.UnitMesh.CrossArmorTargetShellPatchOperationRequest;
using AdaptationSdkStyleTargetShellPatchWorkItem = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchWorkItem;
using AdaptationTargetShellMeshMapping = HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellMeshMapping;
using AdaptationCrossArmorBoneDiagnosticAnalyzer = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.CrossArmorBoneDiagnosticAnalyzer;
using AdaptationCrossArmorTransformInfoExpander = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.CrossArmorTransformInfoExpander;
using AdaptationCrossArmorSkinningDiagnosticAnalyzer = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.CrossArmorSkinningDiagnosticAnalyzer;
using AdaptationSdkStyleAvatarRigReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleAvatarRigReader;
using AdaptationTargetBakeCompatibilityAnalyzer = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.TargetBakeCompatibilityAnalyzer;
using AdaptationTargetBakeDryRunAnalyzer = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.TargetBakeDryRunAnalyzer;
using AdaptationCurrentGameStreamLayoutRegistry = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.ICurrentGameStreamLayoutRegistry;

namespace HD2ModCore.Infrastructure;

// Purpose: Rebuilds current target shells from an approved cross-armor plan into an isolated test Patch.
public sealed class CrossArmorTransferCandidateService : ICrossArmorTransferCandidateService
{
	private const int TargetUnitBatchSize = 8;
	private readonly AdaptationPatchTocScanner scanner = new();
	private readonly AdaptationPatchUnitMeshReader unitReader = new();
	private readonly AdaptationCrossArmorTargetShellPatchOperation patchOperation = new();
	private readonly AdaptationCrossArmorBoneDiagnosticAnalyzer boneDiagnosticAnalyzer = new();
	private readonly AdaptationCrossArmorTransformInfoExpander transformInfoExpander = new();
	private readonly AdaptationCrossArmorSkinningDiagnosticAnalyzer skinningDiagnosticAnalyzer = new();
	private readonly AdaptationTargetBakeCompatibilityAnalyzer targetBakeCompatibilityAnalyzer = new();
	private readonly AdaptationTargetBakeDryRunAnalyzer targetBakeDryRunAnalyzer = new();
	private readonly AdaptationMaterialDependencyResolver materialDependencyResolver = new();
	private readonly IAssetArchiveIndexService assetIndex;

	public CrossArmorTransferCandidateService(IAssetArchiveIndexService? assetIndex = null)
	{
		this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex), "Cross-armor reconstruction requires the current Game Data asset index.");
	}

	public async ValueTask<CrossArmorTransferCandidateResult> GenerateCandidateAsync(CrossArmorTransferCandidateRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var issues = new List<CoreIssue>();
		var diagnosticPath = Path.Combine(request.OutputDirectory, "cross-armor-rebuild-diagnostics.jsonl");
		var performancePath = Path.Combine(request.OutputDirectory, "cross-armor-performance.jsonl");
		var totalStopwatch = Stopwatch.StartNew();
		var wroteDiagnostics = false;
		var outputTransferLayoutDiagnostics = new List<object>();
		if (!request.Plan.CanContinue) return Failure("PlanNotReady", "当前计划尚不可写出；请先选择来源、目标并排除所有错误。", issues);
		if (!File.Exists(request.SourcePatchTocPath)) return Failure("SourcePatchMissing", "源 Patch 主文件不存在。", issues);
		if (!Directory.Exists(request.GameDataDirectory)) return Failure("GameDataMissing", "Game Data 文件夹不存在。", issues);
		try
		{
			Directory.CreateDirectory(request.OutputDirectory);
			if (File.Exists(performancePath)) File.Delete(performancePath);
			await ReportProgressAsync(request, performancePath, "正在读取游戏索引", 0, 1, totalStopwatch, cancellationToken).ConfigureAwait(false);
			var stageStopwatch = Stopwatch.StartNew();
			var indexedLayouts = await assetIndex.GetStreamLayoutsAsync(cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "读取游戏索引", stageStopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
			if (indexedLayouts.Count == 0) throw new InvalidDataException("当前游戏资产索引不含 stream ABI 声明；请先重新建立游戏资产索引后再生成跨护甲候选。");
			AdaptationCurrentGameStreamLayoutRegistry streamLayoutRegistry = new CurrentGameStreamLayoutRegistry(indexedLayouts);
			var sourceEntries = request.PreparedSourceEntries is { Count: > 0 }
				? request.PreparedSourceEntries
				: await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
			var mappings = request.Plan.Mappings.Where(mapping => mapping.WillReplace).ToArray();
			if (File.Exists(diagnosticPath)) File.Delete(diagnosticPath);
			stageStopwatch.Restart();
			await ReportProgressAsync(request, performancePath, "正在准备来源与材质依赖", 0, 1, totalStopwatch, cancellationToken).ConfigureAwait(false);
			await WritePlanAuditAsync(request.OutputDirectory, request.Plan, cancellationToken).ConfigureAwait(false);
			var sourceEntriesByKey = sourceEntries.ToDictionary(entry => entry.AssetKey);
			var sourceKeys = mappings.Select(mapping => ToAdaptationKey(mapping.Source!.UnitAssetKey)).ToHashSet();
			if (!sourceKeys.All(sourceEntriesByKey.ContainsKey)) throw new InvalidDataException("源 Patch 已变化或缺少计划中的真实来源 Unit；请重新打开并确认计划。");
			var sourceUnits = await ReadSourceUnitsAsync(sourceKeys, sourceEntriesByKey, sourceEntries, cancellationToken).ConfigureAwait(false);
			var requestedMaterialIds = CollectMappedSourceMaterialIds(mappings, sourceUnits);
			var materialDependencies = await materialDependencyResolver.ResolveAsync(
				requestedMaterialIds,
				sourceEntries,
				request.GameDataDirectory,
				new Dictionary<AdaptationAssetKey, IReadOnlyList<string>>(),
				cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "准备来源与材质依赖", stageStopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
			IReadOnlySet<ulong>? allowedMaterialIds = null;
			if (request.MaterialBindingMode == CrossArmorMaterialBindingMode.RequireCompleteSourceClosure)
			{
				allowedMaterialIds = requestedMaterialIds.Except(materialDependencies.RejectedMaterialReasons.Keys).ToHashSet();
			}
			var resolver = new AdaptationGameDataPackageResolver(request.GameDataDirectory);
			var avatarRig = await new AdaptationSdkStyleAvatarRigReader(resolver).ReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
			var targetReader = new AdaptationGameDataUnitMeshReader(resolver);
			// Only rewrite Units that have at least one approved replacement. Rebuilding an
			// entirely hidden Unit as a placeholder is both unnecessary and unsafe: static
			// shells can carry older normal layouts that must remain paired with their
			// original GPU data.
			var targetGroups = mappings.GroupBy(mapping => mapping.PhysicalTarget.UnitAssetKey).OrderBy(group => group.Key.FileId).ToArray();
			var batchOutputs = new List<HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput>();
			var batches = targetGroups.Chunk(TargetUnitBatchSize).ToArray();
			var targetReadDuration = TimeSpan.Zero;
			var modelPreparationDuration = TimeSpan.Zero;
			var diagnosticsDuration = TimeSpan.Zero;
			var outputBuildDuration = TimeSpan.Zero;
			for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
			{
				var batch = batches[batchIndex];
				stageStopwatch.Restart();
				await ReportProgressAsync(request, performancePath, "正在重建目标 Unit", batchIndex, batches.Length, totalStopwatch, cancellationToken).ConfigureAwait(false);
				var batchDiagnosticLines = new List<string>();
				var workItems = new List<AdaptationSdkStyleTargetShellPatchWorkItem>(batch.Length);
				var batchTargetReadDuration = TimeSpan.Zero;
				var batchModelPreparationDuration = TimeSpan.Zero;
				var batchDiagnosticsDuration = TimeSpan.Zero;
				foreach (var group in batch)
				{
					var targetArchiveId = FindTargetArchiveId(request.Plan, group.First().PhysicalTarget);
					if (targetArchiveId is null) throw new InvalidDataException($"目标 Unit 0x{group.Key.FileId:x16} 未关联到所选目标 archive。");
					var targetKey = ToAdaptationKey(group.Key);
					var operationStopwatch = Stopwatch.StartNew();
					var targetUnit = await targetReader.ReadAsync(targetArchiveId, targetKey, allowGlobalDependencySearch: true, cancellationToken: cancellationToken).ConfigureAwait(false);
					batchTargetReadDuration += operationStopwatch.Elapsed;
					operationStopwatch.Restart();
					var unitMappings = group.Where(mapping => mapping.WillReplace).Select(mapping => new AdaptationTargetShellMeshMapping(ToAdaptationKey(mapping.Source!.UnitAssetKey), mapping.Source.MeshInfoIndex, mapping.PhysicalTarget.MeshInfoIndex)).ToArray();
					var effectiveUnitMappings = ExpandCompleteLodFamilyMappings(targetUnit.Model, sourceUnits, unitMappings);
					var requiredSources = effectiveUnitMappings.Select(mapping => sourceUnits[mapping.SourceUnitAssetKey]).Distinct().ToArray();
					var expandedTargetModel = targetUnit.Model;
					foreach (var mapping in effectiveUnitMappings) expandedTargetModel = transformInfoExpander.Expand(expandedTargetModel, mapping.TargetMeshInfoIndex, sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex, avatarRig.TransformInfo);
					targetUnit = targetUnit with { Model = expandedTargetModel };
					batchModelPreparationDuration += operationStopwatch.Elapsed;
					operationStopwatch.Restart();
					foreach (var mapping in effectiveUnitMappings)
					{
					var targetBake = targetBakeCompatibilityAnalyzer.Analyze(targetUnit.Model, mapping.TargetMeshInfoIndex, sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex, avatarRig);
						batchDiagnosticLines.Add(JsonSerializer.Serialize(new
					{
							Kind = "TargetBake",
						TargetUnit = $"0x{targetKey.FileId:x16}",
						mapping.TargetMeshInfoIndex,
						SourceUnit = $"0x{mapping.SourceUnitAssetKey.FileId:x16}",
						mapping.SourceMeshInfoIndex,
						Diagnostic = targetBake
						}));
					var targetBakeDryRun = targetBakeDryRunAnalyzer.Analyze(targetUnit.Model, mapping.TargetMeshInfoIndex, sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex, avatarRig);
						batchDiagnosticLines.Add(JsonSerializer.Serialize(new
					{
							Kind = "TargetBakeDryRun",
						TargetUnit = $"0x{targetKey.FileId:x16}",
						mapping.TargetMeshInfoIndex,
						SourceUnit = $"0x{mapping.SourceUnitAssetKey.FileId:x16}",
						mapping.SourceMeshInfoIndex,
						Diagnostic = targetBakeDryRun
						}));
						batchDiagnosticLines.Add(JsonSerializer.Serialize(new { Kind = "TransferLayout", Value = CreateTransferLayoutDiagnostic(
						targetKey,
						mapping.TargetMeshInfoIndex,
						mapping.SourceUnitAssetKey,
						mapping.SourceMeshInfoIndex,
						targetUnit.Model,
						sourceUnits[mapping.SourceUnitAssetKey].Model) }));
					var skinning = skinningDiagnosticAnalyzer.Analyze(sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex);
						batchDiagnosticLines.Add(JsonSerializer.Serialize(new
					{
							Kind = "Skinning",
						TargetUnit = $"0x{targetKey.FileId:x16}",
						mapping.TargetMeshInfoIndex,
						SourceUnit = $"0x{mapping.SourceUnitAssetKey.FileId:x16}",
						mapping.SourceMeshInfoIndex,
						Diagnostic = skinning
						}));
					var diagnostic = boneDiagnosticAnalyzer.Analyze(targetUnit.Model, mapping.TargetMeshInfoIndex, sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex);
						batchDiagnosticLines.Add(JsonSerializer.Serialize(new
					{
							Kind = "Bone",
						TargetUnit = $"0x{targetKey.FileId:x16}",
						mapping.TargetMeshInfoIndex,
						SourceUnit = $"0x{mapping.SourceUnitAssetKey.FileId:x16}",
						mapping.SourceMeshInfoIndex,
						Diagnostic = diagnostic
						}));
					}
					batchDiagnosticsDuration += operationStopwatch.Elapsed;
					workItems.Add(new AdaptationSdkStyleTargetShellPatchWorkItem(
						targetUnit,
						requiredSources,
						effectiveUnitMappings,
						targetUnit.CompositePayload is null
							? HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellDependencyPolicy.ReferenceCurrentGame
							: HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellDependencyPolicy.EmbedReferencedComposite));
				}
				if (batchDiagnosticLines.Count != 0)
				{
					await File.AppendAllLinesAsync(diagnosticPath, batchDiagnosticLines, cancellationToken).ConfigureAwait(false);
					wroteDiagnostics = true;
				}
				var outputBuildStopwatch = Stopwatch.StartNew();
				batchOutputs.Add(patchOperation.BuildOutput(workItems, allowedMaterialIds, avatarRig.TransformInfo.NameHashes, streamLayoutRegistry));
				var batchOutputBuildDuration = outputBuildStopwatch.Elapsed;
				targetReadDuration += batchTargetReadDuration;
				modelPreparationDuration += batchModelPreparationDuration;
				diagnosticsDuration += batchDiagnosticsDuration;
				outputBuildDuration += batchOutputBuildDuration;
				await WritePerformanceAsync(performancePath, $"批次 {batchIndex + 1}/{batches.Length} - 读取目标 Unit", batchTargetReadDuration, cancellationToken).ConfigureAwait(false);
				await WritePerformanceAsync(performancePath, $"批次 {batchIndex + 1}/{batches.Length} - 准备模型与 LOD", batchModelPreparationDuration, cancellationToken).ConfigureAwait(false);
				await WritePerformanceAsync(performancePath, $"批次 {batchIndex + 1}/{batches.Length} - 安全诊断", batchDiagnosticsDuration, cancellationToken).ConfigureAwait(false);
				await WritePerformanceAsync(performancePath, $"批次 {batchIndex + 1}/{batches.Length} - 重建输出", batchOutputBuildDuration, cancellationToken).ConfigureAwait(false);
				await WritePerformanceAsync(performancePath, $"重建批次 {batchIndex + 1}/{batches.Length}（目标 Unit {batch.Length}）", stageStopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
			}
			await WritePerformanceAsync(performancePath, "重建汇总 - 读取目标 Unit", targetReadDuration, cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "重建汇总 - 准备模型与 LOD", modelPreparationDuration, cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "重建汇总 - 安全诊断", diagnosticsDuration, cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "重建汇总 - 重建输出", outputBuildDuration, cancellationToken).ConfigureAwait(false);
			await ReportProgressAsync(request, performancePath, "正在写入最终 Patch", batches.Length, batches.Length, totalStopwatch, cancellationToken).ConfigureAwait(false);
			var headerArchiveId = request.Plan.SelectedTargets.First().ArchiveId;
			var headerTemplate = await resolver.GetPackageTocAsync(headerArchiveId, cancellationToken).ConfigureAwait(false)
				?? throw new FileNotFoundException("无法读取所选目标 archive 的 current TOC。", headerArchiveId);
			var combinedOutput = CombineBatchOutputs(batchOutputs);
			stageStopwatch.Restart();
			var execution = await patchOperation.ExecuteOutputAsync(new AdaptationCrossArmorTargetShellPatchOperationRequest(
				request.SourcePatchTocPath,
				request.OutputDirectory,
				headerTemplate.Data,
				Array.Empty<AdaptationSdkStyleTargetShellPatchWorkItem>(),
				materialDependencies.Entries,
				request.MaterialBindingMode == CrossArmorMaterialBindingMode.RequireCompleteSourceClosure,
				allowedMaterialIds,
				sourceEntries,
				avatarRig.TransformInfo.NameHashes,
				streamLayoutRegistry), combinedOutput, cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "写入最终 Patch", stageStopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
			stageStopwatch.Restart();
			var validationGroups = mappings.GroupBy(mapping => mapping.PhysicalTarget.UnitAssetKey).ToArray();
			await ReportProgressAsync(request, performancePath, "正在回读验证输出", 0, validationGroups.Length, totalStopwatch, cancellationToken).ConfigureAwait(false);
			var outputEntries = await scanner.ScanEntriesAsync(execution.WriteResult.TocFilePath, cancellationToken).ConfigureAwait(false);
			for (var groupIndex = 0; groupIndex < validationGroups.Length; groupIndex++)
			{
				var group = validationGroups[groupIndex];
				var targetKey = ToAdaptationKey(group.Key);
				var outputEntry = outputEntries.SingleOrDefault(entry => entry.AssetKey == targetKey)
					?? throw new InvalidDataException($"输出 Patch 缺少目标 Unit 0x{targetKey.FileId:x16}。" );
				var outputUnit = await unitReader.ReadAsync(outputEntry, outputEntries, cancellationToken: cancellationToken).ConfigureAwait(false);
				EnsureOutputStreamAbi(outputUnit.Model, targetKey);
				foreach (var mapping in group)
				{
					var sourceKey = ToAdaptationKey(mapping.Source!.UnitAssetKey);
					var sourceUnit = sourceUnits[sourceKey];
					EnsureOutputPreservesSourceVertexColor(outputUnit.Model, mapping.PhysicalTarget.MeshInfoIndex, sourceUnit.Model, mapping.Source.MeshInfoIndex);
					EnsureOutputPreservesSourceGeometry(outputUnit.Model, mapping.PhysicalTarget.MeshInfoIndex, sourceUnit.Model, mapping.Source.MeshInfoIndex);
					outputTransferLayoutDiagnostics.Add(CreateTransferLayoutDiagnostic(
						targetKey,
						mapping.PhysicalTarget.MeshInfoIndex,
						sourceKey,
						mapping.Source.MeshInfoIndex,
						outputUnit.Model,
						sourceUnit.Model));
				}
				if ((groupIndex + 1) % 16 == 0 || groupIndex + 1 == validationGroups.Length)
					await ReportProgressAsync(request, performancePath, "正在回读验证输出", groupIndex + 1, validationGroups.Length, totalStopwatch, cancellationToken).ConfigureAwait(false);
			}
			await WritePerformanceAsync(performancePath, "回读验证输出", stageStopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
			var reportPath = await WriteReportAsync(request, execution.WriteResult.TocFilePath, execution.Output, Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), outputTransferLayoutDiagnostics, requestedMaterialIds, materialDependencies, cancellationToken).ConfigureAwait(false);
			await WritePerformanceAsync(performancePath, "总耗时", totalStopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
			await ReportProgressAsync(request, performancePath, "生成完成", 1, 1, totalStopwatch, cancellationToken).ConfigureAwait(false);
			return new CrossArmorTransferCandidateResult(true, request.OutputDirectory, reportPath, execution.Output.UnitResults.Count, execution.Output.UnitResults.Sum(result => result.ReplacementCount), execution.Output.UnitResults.Sum(result => result.MinifiedCount), issues);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			if (wroteDiagnostics && !string.IsNullOrWhiteSpace(request.OutputDirectory))
			{
				Directory.CreateDirectory(request.OutputDirectory);
				await WriteFailureDiagnosticAsync(request.OutputDirectory, exception, Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), request.Plan, cancellationToken).ConfigureAwait(false);
			}
			return Failure("CrossArmorWriteFailed", exception.Message, issues, request.OutputDirectory);
		}
	}

	private static async ValueTask ReportProgressAsync(CrossArmorTransferCandidateRequest request, string performancePath, string stage, int completed, int total, Stopwatch stopwatch, CancellationToken cancellationToken)
	{
		request.Progress?.Report(new CrossArmorTransferProgress(stage, completed, total, stopwatch.Elapsed));
		await AppendPerformanceEventAsync(performancePath, stage, "Progress", stopwatch.Elapsed, completed, total, cancellationToken).ConfigureAwait(false);
	}

	private static ValueTask WritePerformanceAsync(string path, string stage, TimeSpan elapsed, CancellationToken cancellationToken)
		=> AppendPerformanceEventAsync(path, stage, "Duration", elapsed, null, null, cancellationToken);

	private static async ValueTask AppendPerformanceEventAsync(string path, string stage, string kind, TimeSpan elapsed, int? completed, int? total, CancellationToken cancellationToken)
	{
		await AppendDiagnosticAsync(path, new { TimestampUtc = DateTimeOffset.UtcNow, Kind = kind, Stage = stage, ElapsedMilliseconds = (long)elapsed.TotalMilliseconds, Completed = completed, Total = total }, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<IReadOnlyDictionary<AdaptationAssetKey, AdaptationPatchUnitMesh>> ReadSourceUnitsAsync(
		IReadOnlyCollection<AdaptationAssetKey> sourceKeys,
		IReadOnlyDictionary<AdaptationAssetKey, HD2ModAdaptation.PatchReconstruction.PatchTocEntry> sourceEntriesByKey,
		IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry> sourceEntries,
		CancellationToken cancellationToken)
	{
		var result = new Dictionary<AdaptationAssetKey, AdaptationPatchUnitMesh>(sourceKeys.Count);
		foreach (var key in sourceKeys.OrderBy(key => key.FileId))
		{
			if (!sourceEntriesByKey.TryGetValue(key, out var entry)) throw new InvalidDataException($"源 Patch 缺少 Unit 0x{key.FileId:x16}。");
			result.Add(key, await unitReader.ReadAsync(entry, sourceEntries, cancellationToken: cancellationToken).ConfigureAwait(false));
		}
		return result;
	}

	private static async ValueTask AppendDiagnosticAsync(string path, object diagnostic, CancellationToken cancellationToken)
	{
		var json = JsonSerializer.Serialize(diagnostic);
		await File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
	}

	private static HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput CombineBatchOutputs(
		IReadOnlyCollection<HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput> outputs)
	{
		var additions = outputs.SelectMany(output => output.AdditionalEntries).GroupBy(entry => entry.AssetKey).Select(group => group.Single()).ToArray();
		var replaced = outputs.SelectMany(output => output.ReplacedSourceUnitAssetKeys).Distinct().OrderBy(key => key.FileId).ToArray();
		var results = outputs.SelectMany(output => output.UnitResults).OrderBy(result => result.TargetUnitAssetKey.FileId).ToArray();
		if (results.GroupBy(result => result.TargetUnitAssetKey).Any(group => group.Count() != 1)) throw new InvalidDataException("跨护甲分批重建产生了重复目标 Unit。");
		return new HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput(additions, replaced, results);
	}

	private static IReadOnlyCollection<ulong> CollectMappedSourceMaterialIds(
		IReadOnlyCollection<CrossArmorTransferMapping> mappings,
		IReadOnlyDictionary<AdaptationAssetKey, AdaptationPatchUnitMesh> sourceUnits)
	{
		var result = new HashSet<ulong>();
		foreach (var mapping in mappings)
		{
			var source = mapping.Source!;
			var model = sourceUnits[ToAdaptationKey(source.UnitAssetKey)].Model;
			var mesh = model.Meshes.FirstOrDefault(item => item.Index == source.MeshInfoIndex)
				?? throw new KeyNotFoundException($"源 Unit 0x{source.UnitAssetKey.FileId:x16} 不包含 MeshInfo {source.MeshInfoIndex}。");
			var rawMesh = model.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == source.MeshInfoIndex)
				?? throw new KeyNotFoundException($"源 Unit 0x{source.UnitAssetKey.FileId:x16} 不包含 mesh {source.MeshInfoIndex}。");
			foreach (var section in rawMesh.Sections)
			{
				if (section.MaterialIndex >= mesh.MaterialSlotIds.Count) continue;
				var slot = mesh.MaterialSlotIds[(int)section.MaterialIndex];
				foreach (var material in model.Materials.Where(binding => binding.SectionId == slot)) result.Add(material.MaterialId);
			}
		}
		return result.OrderBy(id => id).ToArray();
	}

	private static string? FindTargetArchiveId(CrossArmorTransferPlan plan, CrossArmorPhysicalTargetKey target)
		=> plan.SelectedTargets.FirstOrDefault(entry => entry.Parts.Any(part => part.UnitAssetKey == target.UnitAssetKey && part.MeshInfoIndex == target.MeshInfoIndex))?.ArchiveId;

	private static IReadOnlyList<AdaptationTargetShellMeshMapping> ExpandCompleteLodFamilyMappings(
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel targetModel,
		IReadOnlyDictionary<AdaptationAssetKey, AdaptationPatchUnitMesh> sourceUnits,
		IReadOnlyList<AdaptationTargetShellMeshMapping> approvedMappings)
	{
		// A selected visual LOD0 is only one member of a Unit's render family. Rebuild
		var targetRenderFamily = targetModel.RawMeshData
			.Where(mesh => mesh.LodIndex is >= 0 and <= 4)
			.OrderBy(mesh => mesh.MeshInfoIndex)
			.ToArray();
		if (targetRenderFamily.Length < 2) return approvedMappings;

		var expanded = new List<AdaptationTargetShellMeshMapping>(approvedMappings);
		var coveredTargets = expanded.Select(mapping => mapping.TargetMeshInfoIndex).ToHashSet();
		foreach (var approved in approvedMappings)
		{
			var sourceModel = sourceUnits[approved.SourceUnitAssetKey].Model;
			var sourceRenderFamily = sourceModel.RawMeshData
				.Where(mesh => mesh.LodIndex is -1 or >= 0 and <= 4)
				.OrderBy(mesh => mesh.MeshInfoIndex)
				.ToArray();
			var targetLod0 = targetRenderFamily.SingleOrDefault(mesh => mesh.LodIndex == 0);
			var sourceLod0 = sourceRenderFamily.SingleOrDefault(mesh => mesh.LodIndex == 0)
				?? sourceRenderFamily.SingleOrDefault(mesh => mesh.MeshInfoIndex == approved.SourceMeshInfoIndex && mesh.LodIndex == -1);
			if (targetLod0 is null || sourceLod0 is null
				|| approved.TargetMeshInfoIndex != targetLod0.MeshInfoIndex
				|| approved.SourceMeshInfoIndex != sourceLod0.MeshInfoIndex) continue;

			var sourceByLod = sourceRenderFamily
				.GroupBy(mesh => mesh.LodIndex)
				.ToDictionary(group => group.Key, group => group.ToArray());
			foreach (var targetMesh in targetRenderFamily)
			{
				if (coveredTargets.Contains(targetMesh.MeshInfoIndex)) continue;
				var sourceLod = targetMesh.LodIndex == 4 ? -1 : targetMesh.LodIndex;
				var sourceMesh = sourceByLod.TryGetValue(sourceLod, out var sourceCandidates) && sourceCandidates.Length == 1
					? sourceCandidates[0]
					: sourceModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == approved.SourceMeshInfoIndex);
				if (!HasCompatibleEffectiveSectionLayout(sourceMesh, targetMesh)) continue;
				expanded.Add(new AdaptationTargetShellMeshMapping(approved.SourceUnitAssetKey, sourceMesh.MeshInfoIndex, targetMesh.MeshInfoIndex));
				coveredTargets.Add(targetMesh.MeshInfoIndex);
			}
		}

		return expanded;
	}

	private static bool HasCompatibleEffectiveSectionLayout(
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitRawMeshData source,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitRawMeshData target)
		=> source.Sections.Count(section => section.Triangles.Count != 0) == target.Sections.Count(section => section.Triangles.Count != 0)
			&& source.Sections.Count(section => section.Triangles.Count != 0) != 0;

	private static async ValueTask<string> WriteReportAsync(
		CrossArmorTransferCandidateRequest request,
		string tocPath,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput output,
		IReadOnlyList<object> boneDiagnostics,
		IReadOnlyList<object> skinningDiagnostics,
		IReadOnlyList<object> targetBakeDiagnostics,
		IReadOnlyList<object> targetBakeDryRunDiagnostics,
		IReadOnlyList<object> transferLayoutDiagnostics,
		IReadOnlyList<object> outputTransferLayoutDiagnostics,
		IReadOnlyCollection<ulong> requestedMaterialIds,
		HD2ModAdaptation.PatchReconstruction.MaterialDependencyResolutionResult materialDependencies,
		CancellationToken cancellationToken)
	{
		var path = Path.Combine(request.OutputDirectory, "cross-armor-transfer-report.json");
		var report = new
		{
			GeneratedUtc = DateTimeOffset.UtcNow,
			SourcePatch = request.SourcePatchTocPath,
			OutputPatch = tocPath,
			IsolatedCandidate = true,
			MaterialPolicy = request.MaterialBindingMode == CrossArmorMaterialBindingMode.PreserveSourceReferences
				? "Source material bindings are preserved directly on rebuilt target slots. Existing non-Unit source Material and Texture entries remain in the output without dependency-closure validation."
				: "Closure-complete source materials are embedded and propagated per target slot; unresolved source materials fall back to current target bindings.",
			Materials = new
			{
				BindingMode = request.MaterialBindingMode.ToString(),
				Requested = requestedMaterialIds.Select(id => $"0x{id:x16}").ToArray(),
				Propagated = output.UnitResults.SelectMany(result => result.ReplacementMaterialIds).Distinct().OrderBy(id => id).Select(id => $"0x{id:x16}").ToArray(),
				Rejected = materialDependencies.RejectedMaterialReasons.OrderBy(pair => pair.Key).Select(pair => new { Material = $"0x{pair.Key:x16}", Reason = pair.Value }).ToArray(),
				ResolvedDependencyCount = materialDependencies.Entries.Count,
				EmbeddedDependencyCount = request.MaterialBindingMode == CrossArmorMaterialBindingMode.RequireCompleteSourceClosure ? materialDependencies.Entries.Count : 0,
				Origins = materialDependencies.Origins.OrderBy(pair => pair.Key.TypeId).ThenBy(pair => pair.Key.FileId).Select(pair => new { Asset = $"0x{pair.Key.TypeId:x16}/0x{pair.Key.FileId:x16}", Kind = pair.Value.Kind.ToString(), pair.Value.Name }).ToArray()
			},
			BoneDiagnostics = boneDiagnostics,
			SkinningDiagnostics = skinningDiagnostics,
			TargetBakeDiagnostics = targetBakeDiagnostics,
			TargetBakeDryRunDiagnostics = targetBakeDryRunDiagnostics,
			CanonicalSkinningRebuilds = targetBakeDryRunDiagnostics
				.Select(diagnostic => JsonSerializer.SerializeToElement(diagnostic))
				.Where(diagnostic => diagnostic.GetProperty("Diagnostic").GetProperty("Status").GetString() != "TargetBakeDryRunReady")
				.Select(diagnostic => new
				{
					TargetUnit = diagnostic.GetProperty("TargetUnit").GetString(),
					TargetMeshInfoIndex = diagnostic.GetProperty("TargetMeshInfoIndex").GetInt32(),
					SourceUnit = diagnostic.GetProperty("SourceUnit").GetString(),
					SourceMeshInfoIndex = diagnostic.GetProperty("SourceMeshInfoIndex").GetInt32(),
					PriorStatus = diagnostic.GetProperty("Diagnostic").GetProperty("Status").GetString(),
					Reason = diagnostic.GetProperty("Diagnostic").GetProperty("BlockReason").GetString(),
					Route = "SdkCanonicalSkinningLayout"
				}).ToArray(),
			TransferLayoutDiagnostics = transferLayoutDiagnostics,
			OutputTransferLayoutDiagnostics = outputTransferLayoutDiagnostics,
			SkinningRisks = skinningDiagnostics
				.Select(diagnostic => JsonSerializer.SerializeToElement(diagnostic))
				.Where(diagnostic => diagnostic.GetProperty("Diagnostic").GetProperty("ZeroActiveWeightVertexCount").GetInt32() != 0
					|| diagnostic.GetProperty("Diagnostic").GetProperty("InvalidActiveInfluenceCount").GetInt32() != 0
					|| diagnostic.GetProperty("Diagnostic").GetProperty("NonFinitePositionCount").GetInt32() != 0)
				.ToArray(),
			Mappings = request.Plan.Mappings.Select(mapping => new
			{
				TargetUnit = $"0x{mapping.PhysicalTarget.UnitAssetKey.FileId:x16}",
				mapping.PhysicalTarget.MeshInfoIndex,
				Target = new { mapping.Target.PartKind, mapping.Target.Layer, mapping.Target.BodyVariant, mapping.Target.SemanticName },
				SourceUnit = mapping.Source is null ? null : $"0x{mapping.Source.UnitAssetKey.FileId:x16}",
				SourceMeshInfoIndex = mapping.Source?.MeshInfoIndex,
				Source = mapping.Source is null ? null : new { mapping.Source.PartKind, mapping.Source.Layer, mapping.Source.BodyVariant, mapping.Source.SemanticName },
				mapping.WillReplace,
				mapping.IsManual,
				mapping.IsSuppressed,
				mapping.Reason
			}).ToArray(),
			Units = output.UnitResults.Select(result => new { TargetUnit = $"0x{result.TargetUnitAssetKey.FileId:x16}", result.ReplacementCount, result.MinifiedCount, result.CoveredTargetMeshCount }).ToArray()
		};
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
		return path;
	}

	private static object CreateTransferLayoutDiagnostic(
		AdaptationAssetKey targetUnitKey,
		int targetMeshInfoIndex,
		AdaptationAssetKey sourceUnitKey,
		int sourceMeshInfoIndex,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel targetModel,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel sourceModel)
	{
		var sourceMesh = sourceModel.Meshes.Single(mesh => mesh.Index == sourceMeshInfoIndex);
		var targetMesh = targetModel.Meshes.Single(mesh => mesh.Index == targetMeshInfoIndex);
		var sourceRawMesh = sourceModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex);
		var targetRawMesh = targetModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == targetMeshInfoIndex);
		var sourceStream = sourceModel.Streams.Single(stream => stream.Index == sourceRawMesh.StreamIndex);
		var targetStream = targetModel.Streams.Single(stream => stream.Index == targetRawMesh.StreamIndex);
		var sourceSections = sourceRawMesh.Sections.Select((section, index) => DescribeSection(index, section, sourceMesh, sourceModel.Materials)).ToArray();
		var targetSections = targetRawMesh.Sections.Select((section, index) => DescribeSection(index, section, targetMesh, targetModel.Materials)).ToArray();
		var assignments = sourceSections.Select(source =>
		{
			var target = targetSections[source.Index % targetSections.Length];
			return new { SourceSectionIndex = source.Index, SourceMaterialId = source.MaterialId, TargetSectionIndex = target.Index, TargetMaterialId = target.MaterialId, TargetMaterialSlot = target.MaterialSlot };
		}).ToArray();
		var targetSectionCollisions = assignments
			.GroupBy(assignment => assignment.TargetSectionIndex)
			.Select(group => new
			{
				TargetSectionIndex = group.Key,
				SourceSectionIndexes = group.Select(item => item.SourceSectionIndex).ToArray(),
				SourceMaterialIds = group.Select(item => item.SourceMaterialId).Distinct().OrderBy(id => id).ToArray(),
				CanPreserveOneSourceMaterial = group.Select(item => item.SourceMaterialId).Distinct().Count() == 1
			})
			.Where(group => group.SourceSectionIndexes.Length > 1 || !group.CanPreserveOneSourceMaterial)
			.ToArray();
		return new
		{
			TargetUnit = $"0x{targetUnitKey.FileId:x16}",
			TargetMeshInfoIndex = targetMeshInfoIndex,
			SourceUnit = $"0x{sourceUnitKey.FileId:x16}",
			SourceMeshInfoIndex = sourceMeshInfoIndex,
			Source = new { StreamIndex = sourceRawMesh.StreamIndex, ComponentInfoId = $"0x{sourceStream.ComponentInfoId:x16}", sourceStream.VertexBufferId, sourceStream.IndexBufferId, sourceStream.VertexStride, SectionCount = sourceSections.Length, MaterialSlotCount = sourceMesh.MaterialSlotIds.Count, Sections = sourceSections, Components = DescribeComponents(sourceStream.Components) },
			Target = new { StreamIndex = targetRawMesh.StreamIndex, ComponentInfoId = $"0x{targetStream.ComponentInfoId:x16}", targetStream.VertexBufferId, targetStream.IndexBufferId, targetStream.VertexStride, SectionCount = targetSections.Length, MaterialSlotCount = targetMesh.MaterialSlotIds.Count, Sections = targetSections, Components = DescribeComponents(targetStream.Components) },
			SectionAssignments = assignments,
			TargetSectionCollisions = targetSectionCollisions,
			VertexComponentCompatibility = targetStream.Components.Select(target =>
			{
				var exact = sourceStream.Components.FirstOrDefault(source => source.Type == target.Type && source.Index == target.Index);
				var fallback = sourceStream.Components.FirstOrDefault(source => source.Type == target.Type);
				var selected = exact ?? fallback;
				return new
				{
					Target = DescribeComponent(target),
					SourceMatch = selected is null ? null : DescribeComponent(selected),
					MatchKind = exact is not null ? "ExactTypeAndIndex" : fallback is not null ? "TypeOnlyFallback" : "Missing",
					FormatMatches = selected is not null && selected.Format == target.Format,
					SizeMatches = selected is not null && selected.Size == target.Size
				};
			}).ToArray()
		};
	}

	private static void EnsureOutputPreservesSourceVertexColor(
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel outputModel,
		int outputMeshInfoIndex,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		var sourceRawMesh = sourceModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex);
		var outputRawMesh = outputModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == outputMeshInfoIndex);
		var sourceStream = sourceModel.Streams.Single(stream => stream.Index == sourceRawMesh.StreamIndex);
		var outputStream = outputModel.Streams.Single(stream => stream.Index == outputRawMesh.StreamIndex);
		var sourceColor = sourceStream.Components.FirstOrDefault(component => component.Type == 5 && component.Index == 0);
		if (sourceColor is null) return;
		var outputColor = outputStream.Components.FirstOrDefault(component => component.Type == 5 && component.Index == 0);
		if (outputColor is null || outputColor.Format != sourceColor.Format || outputColor.Size != sourceColor.Size)
		{
			throw new InvalidDataException($"输出 target mesh {outputMeshInfoIndex} 未保留来源 vertex color 布局。" );
		}
	}

	private static void EnsureOutputPreservesSourceGeometry(
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel outputModel,
		int outputMeshInfoIndex,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		var source = sourceModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex);
		var output = outputModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == outputMeshInfoIndex);
		var sourceTriangleCount = source.Triangles.Count;
		var outputTriangleCount = output.Triangles.Count;
		if (source.Vertices.Count != output.Vertices.Count || sourceTriangleCount != outputTriangleCount)
		{
			throw new InvalidDataException($"输出 target mesh {outputMeshInfoIndex} 的几何数量与来源不一致：顶点 {output.Vertices.Count}/{source.Vertices.Count}，三角形 {outputTriangleCount}/{sourceTriangleCount}。" );
		}
	}

	private static void EnsureOutputStreamAbi(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel outputModel, AdaptationAssetKey targetKey)
	{
		foreach (var stream in outputModel.Streams)
		{
			var componentSize = checked((uint)stream.Components.Sum(component => component.Size));
			if (stream.VertexStride != componentSize) throw new InvalidDataException($"输出 Unit 0x{targetKey.FileId:x16} 的 stream {stream.Index} stride 与分量大小不一致。" );
			foreach (var weight in stream.Components.Where(component => component.Type == 7))
			{
				if (weight.Format != 35 || weight.Size != 8) throw new InvalidDataException($"输出 Unit 0x{targetKey.FileId:x16} 的 stream {stream.Index} 含非 canonical bone_weight 格式 {weight.Format}。" );
			}
			foreach (var index in stream.Components.Where(component => component.Type == 6))
			{
				if (index.Format != 28 || index.Size != 4) throw new InvalidDataException($"输出 Unit 0x{targetKey.FileId:x16} 的 stream {stream.Index} 含非 canonical bone_index 格式 {index.Format}。" );
			}
		}
	}

	private static TransferSectionDiagnostic DescribeSection(
		int index,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitRawMeshSectionData section,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshInfo mesh,
		IReadOnlyList<HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMaterialBinding> bindings)
	{
		var materialSlot = section.MaterialIndex < mesh.MaterialSlotIds.Count
			? mesh.MaterialSlotIds[(int)section.MaterialIndex]
			: section.MaterialSlotId;
		var materialIds = bindings.Where(binding => binding.SectionId == materialSlot).Select(binding => binding.MaterialId).Distinct().OrderBy(id => id).ToArray();
		return new TransferSectionDiagnostic(index, section.MaterialIndex, materialSlot, section.MaterialSlotId, section.Triangles.Count, materialIds.Length == 1 ? $"0x{materialIds[0]:x16}" : null, materialIds.Select(id => $"0x{id:x16}").ToArray());
	}

	private static object[] DescribeComponents(IReadOnlyList<HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitStreamComponentInfo> components)
		=> components.Select(DescribeComponent).ToArray();

	private static object DescribeComponent(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitStreamComponentInfo component)
		=> new { component.Type, component.TypeName, component.Index, component.Format, component.FormatName, component.Size, Unknown = $"0x{component.Unknown:x16}" };

	private sealed record TransferSectionDiagnostic(int Index, uint MaterialIndex, uint MaterialSlot, uint RawMaterialSlot, int TriangleCount, string? MaterialId, IReadOnlyList<string> MaterialIds);

	private static async ValueTask WritePlanAuditAsync(string outputDirectory, CrossArmorTransferPlan plan, CancellationToken cancellationToken)
	{
		var path = Path.Combine(outputDirectory, "cross-armor-plan-audit.json");
		var audit = new
		{
			Format = "hd2-cross-armor-plan-audit-v2",
			GeneratedUtc = DateTimeOffset.UtcNow,
			SelectedTargets = plan.SelectedTargets.Select(target => new { target.ArchiveId, target.DisplayName, target.Category }).ToArray(),
			Summary = new
			{
				PhysicalTargetCount = plan.Mappings.Count,
				ReplacementCount = plan.Mappings.Count(mapping => mapping.WillReplace),
				HitCount = plan.Mappings.Sum(mapping => mapping.HitCount),
				SharedPhysicalTargetCount = plan.Mappings.Count(mapping => mapping.UsedByArchiveIds.Count > 1)
			},
			Mappings = plan.Mappings.Select(mapping => new
			{
				PhysicalTarget = new { Unit = $"0x{mapping.PhysicalTarget.UnitAssetKey.FileId:x16}", mapping.PhysicalTarget.MeshInfoIndex },
				Target = DescribePart(mapping.Target),
				Source = mapping.Source is null ? null : DescribePart(mapping.Source),
				mapping.WillReplace,
				mapping.HitCount,
				mapping.IsManual,
				mapping.IsSuppressed,
				mapping.Reason,
				mapping.UsedByArchiveIds,
				mapping.UsedByDisplayNames,
				SharedPhysicalTarget = mapping.UsedByArchiveIds.Count > 1,
				BodyVariantExact = mapping.Source?.BodyVariant == mapping.Target.BodyVariant,
				UsesAnyBodyVariant = mapping.Source?.BodyVariant == UnitMeshBodyVariant.Any || mapping.Target.BodyVariant == UnitMeshBodyVariant.Any,
				LayerExact = mapping.Source?.Layer == mapping.Target.Layer
			}).ToArray()
		};
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
	}

	private static object DescribePart(EquipmentUnitPart part) => new
	{
		Unit = $"0x{part.UnitAssetKey.FileId:x16}",
		part.MeshInfoIndex,
		PartKind = part.PartKind.ToString(),
		Layer = part.Layer.ToString(),
		BodyVariant = part.BodyVariant.ToString(),
		part.SemanticName
	};

	private static async ValueTask WriteFailureDiagnosticAsync(
		string outputDirectory,
		Exception exception,
		IReadOnlyList<object> boneDiagnostics,
		IReadOnlyList<object> skinningDiagnostics,
		IReadOnlyList<object> targetBakeDiagnostics,
		IReadOnlyList<object> targetBakeDryRunDiagnostics,
		IReadOnlyList<object> transferLayoutDiagnostics,
		CrossArmorTransferPlan plan,
		CancellationToken cancellationToken)
	{
		var path = Path.Combine(outputDirectory, "cross-armor-bone-diagnostic.json");
		var report = new
		{
			GeneratedUtc = DateTimeOffset.UtcNow,
			WriteSucceeded = false,
			Failure = exception.Message,
			BoneDiagnostics = boneDiagnostics,
			SkinningDiagnostics = skinningDiagnostics,
			TargetBakeDiagnostics = targetBakeDiagnostics,
			TargetBakeDryRunDiagnostics = targetBakeDryRunDiagnostics,
			TransferLayoutDiagnostics = transferLayoutDiagnostics,
			Mappings = plan.Mappings.Select(mapping => new
			{
				TargetUnit = $"0x{mapping.PhysicalTarget.UnitAssetKey.FileId:x16}",
				mapping.PhysicalTarget.MeshInfoIndex,
				SourceUnit = mapping.Source is null ? null : $"0x{mapping.Source.UnitAssetKey.FileId:x16}",
				SourceMeshInfoIndex = mapping.Source?.MeshInfoIndex,
				mapping.WillReplace,
				mapping.HitCount,
				mapping.Reason,
				mapping.UsedByArchiveIds
			}).ToArray()
		};
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
	}

	private static AdaptationAssetKey ToAdaptationKey(AssetKey key) => new(key.TypeId, key.FileId);
	private static CrossArmorTransferCandidateResult Failure(string code, string message, List<CoreIssue> issues, string? outputDirectory = null)
	{
		issues.Add(new CoreIssue(CoreIssueSeverity.Error, code, message));
		return new CrossArmorTransferCandidateResult(false, outputDirectory, null, 0, 0, 0, issues);
	}
}