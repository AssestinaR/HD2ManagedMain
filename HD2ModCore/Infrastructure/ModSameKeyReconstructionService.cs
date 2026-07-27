using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationSameKeyTargetShellReconstructionOperation = HD2ModAdaptation.PatchReconstruction.UnitMesh.SameKeyTargetShellReconstructionOperation;
using AdaptationSameKeyTargetShellReconstructionOperationContract = HD2ModAdaptation.PatchReconstruction.UnitMesh.ISameKeyTargetShellReconstructionOperation;
using AdaptationSameKeyTargetShellReconstructionRequest = HD2ModAdaptation.PatchReconstruction.UnitMesh.SameKeyTargetShellReconstructionRequest;
using AdaptationSameKeyTargetShellReconstructionUnit = HD2ModAdaptation.PatchReconstruction.UnitMesh.SameKeyTargetShellReconstructionUnit;
using AdaptationTargetShellMeshMapping = HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellMeshMapping;

namespace HD2ModCore.Infrastructure;

// Purpose: Orchestrates a fully current-target same-key reconstruction without permitting the UI to parse or write patch binaries.
public sealed class ModSameKeyReconstructionService : IModSameKeyReconstructionService
{
	private readonly IPatchFileNameParser fileNameParser;
	private readonly ISameKeyReconstructionPlanningService planningService;
	private readonly IAssetArchiveIndexService assetIndex;
	private readonly IArchiveHashesProvider archiveHashes;
	private readonly IAdvancedModAnalysisService advancedAnalysis;
	private readonly AdaptationSameKeyTargetShellReconstructionOperationContract reconstructionOperation;

	public ModSameKeyReconstructionService(
		IPatchFileNameParser fileNameParser,
		ISameKeyReconstructionPlanningService planningService,
		IAssetArchiveIndexService assetIndex,
		IArchiveHashesProvider archiveHashes,
		IAdvancedModAnalysisService advancedAnalysis,
		AdaptationSameKeyTargetShellReconstructionOperationContract? reconstructionOperation = null)
	{
		this.fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		this.planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
		this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex));
		this.archiveHashes = archiveHashes ?? throw new ArgumentNullException(nameof(archiveHashes));
		this.advancedAnalysis = advancedAnalysis ?? throw new ArgumentNullException(nameof(advancedAnalysis));
		this.reconstructionOperation = reconstructionOperation ?? new AdaptationSameKeyTargetShellReconstructionOperation();
	}

	public async ValueTask<ModSameKeyReconstructionState> InspectAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		CancellationToken cancellationToken = default,
		IProgress<OperationProgressEvent>? progress = null,
		Guid? operationId = null)
	{
		var reporter = new SameKeyProgressReporter(progress, operationId);
		var issues = new List<CoreIssue>();
		SameKeyReconstructionPlan? firstPlan = null;
		try
		{
			reporter.Report("InspectEligibility", "正在检查重建资格", 0, 0, OperationState.Started);
			ArgumentNullException.ThrowIfNull(source);
			var patchPaths = FindBasePatchPaths(source, modsRootDirectory);
		if (patchPaths.Count == 0)
		{
			issues.Add(Error("PatchRequired", "Mod 没有 Patch 主文件。", source.Id));
			reporter.Failed("没有可重建的 Patch。");
			return CreateState(source.Id, null, null, false, issues);
		}
		reporter.Report("InspectEligibility", "重建资格检查完成", 1, 1, OperationState.Progress);
		if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
		{
			issues.Add(Error("GameDataMissing", "请先在设置中配置有效的 Game Data 文件夹。", source.Id));
			reporter.Failed("Game Data 不可用。");
			return CreateState(source.Id, patchPaths[0], null, false, issues);
		}

			GameDataIndexStatus indexStatus;
			indexStatus = await assetIndex.GetIndexStatusAsync(gameDataDirectory, await archiveHashes.GetArchiveHashesJsonAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
		if (!indexStatus.IsCurrent)
		{
			issues.Add(Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期；请先在状态页建立/重建资产索引。", source.Id));
			reporter.Failed("Game Data 资产索引不可用。");
			return CreateState(source.Id, patchPaths[0], null, false, issues);
		}

			foreach (var patchPath in patchPaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				reporter.Report("LoadFacts", "正在读取来源与目标数据", 0, patchPaths.Count, OperationState.Started);
				var plan = await CreatePlanAsync(source, patchPath, gameDataDirectory, modsRootDirectory, cancellationToken).ConfigureAwait(false);
				firstPlan ??= plan;
				CollectPlanIssues(plan, source.Id, issues);
				reporter.Report("LoadFacts", "来源与目标数据读取完成", 1, patchPaths.Count, OperationState.Progress);
			}
			reporter.Completed("重建资格检查完成");
			return CreateState(source.Id, patchPaths[0], firstPlan, true, issues);
		}
		catch (OperationCanceledException)
		{
			reporter.Canceled();
			throw;
		}
		catch (Exception exception)
		{
			var nodeId = source?.Id ?? default;
			issues.Add(Error("SameKeyInspectFailed", exception.Message, nodeId, exception));
			reporter.Failed("重建资格检查失败。");
			return CreateState(nodeId, null, null, false, issues);
		}
	}

	public async ValueTask<SameKeyReconstructionOperationResult> GenerateCandidateAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		string outputRootDirectory,
		CancellationToken cancellationToken = default,
		IProgress<OperationProgressEvent>? progress = null,
		Guid? operationId = null)
	{
		var reporter = new SameKeyProgressReporter(progress, operationId);
		var issues = new List<CoreIssue>();
		string? outputDirectory = null;
		string? outputOwnershipMarker = null;
		bool outputCreated = false;
		try
		{
			reporter.Report("InspectEligibility", "正在检查重建资格", 0, 0, OperationState.Started);
			ArgumentNullException.ThrowIfNull(source);
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(outputRootDirectory))
			{
				issues.Add(Error("OutputDirectoryMissing", "必须选择输出文件夹。", source.Id));
				return Fail(reporter, issues);
			}
			var patchPaths = FindBasePatchPaths(source, modsRootDirectory);
			if (patchPaths.Count == 0)
			{
				issues.Add(Error("PatchRequired", "Mod 没有 Patch 主文件。", source.Id));
				return Fail(reporter, issues);
			}
			if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
			{
				issues.Add(Error("GameDataMissing", "请先在设置中配置有效的 Game Data 文件夹。", source.Id));
				return Fail(reporter, issues);
			}

		GameDataIndexStatus indexStatus;
		try
		{
			indexStatus = await assetIndex.GetIndexStatusAsync(gameDataDirectory, await archiveHashes.GetArchiveHashesJsonAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			issues.Add(Error("GameDataIndexUnreadable", exception.Message, source.Id, exception));
			return Fail(reporter, issues);
		}
		if (!indexStatus.IsCurrent)
		{
			issues.Add(Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期；请先在状态页建立/重建资产索引。", source.Id));
			return Fail(reporter, issues);
		}
			reporter.Report("InspectEligibility", "重建资格检查完成", 1, 1, OperationState.Progress);

		var plans = new List<(string PatchPath, SameKeyReconstructionPlan Plan)>();
		try
		{
				reporter.Report("Plan", "正在生成重建计划", 0, patchPaths.Count, OperationState.Started);
			foreach (var patchPath in patchPaths)
			{
					var planningProgress = new Progress<SameKeyPlanningProgress>(update =>
					{
						var detail = string.IsNullOrWhiteSpace(update.UnitAssetKey) ? update.StageText : $"{update.StageText} {update.UnitAssetKey}";
						if (update.Elapsed is { } elapsed) detail += $"，耗时={elapsed.TotalMilliseconds:0}ms";
							reporter.Report(update.StageId, detail, update.Completed, update.Total, OperationState.Progress);
					});
					var plan = await CreatePlanAsync(source, patchPath, gameDataDirectory, modsRootDirectory, cancellationToken, planningProgress).ConfigureAwait(false);
				plans.Add((patchPath, plan));
				CollectPlanIssues(plan, source.Id, issues);
				reporter.Report("Plan", "重建计划已生成", plans.Count, patchPaths.Count, OperationState.Progress);
			}
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			issues.Add(Error("SameKeyPlanFailed", exception.Message, source.Id, exception));
			return Fail(reporter, issues);
		}
		if (plans.All(item => item.Plan.SourceUnitCount == 0) || issues.Any(issue => issue.Severity == CoreIssueSeverity.Error)) return Fail(reporter, issues);

			(outputDirectory, outputOwnershipMarker) = CreateOwnedOutputDirectory(outputRootDirectory, source.Metadata.Name);
			outputCreated = true;
			var executions = new List<(SameKeyReconstructionPlan Plan, HD2ModAdaptation.PatchReconstruction.PatchArchiveFileWriteResult Write)>();
			var buildCandidateProgress = new BuildCandidateProgress(plans.Where(item => item.Plan.SourceUnitCount > 0).Sum(item => item.Plan.Units.Count));
			foreach (var (sourcePath, plan) in plans)
			{
				if (plan.SourceUnitCount == 0) continue;
				var preparedEntries = await GetPreparedSourceEntriesAsync(source, sourcePath, modsRootDirectory, cancellationToken).ConfigureAwait(false);
				var request = new AdaptationSameKeyTargetShellReconstructionRequest(
					sourcePath,
					gameDataDirectory,
					outputDirectory,
					plan.Units.Select(unit =>
				{
					var targetArchive = unit.TargetArchive ?? throw new InvalidDataException($"Unit 0x{unit.UnitAssetKey.FileId:x16} has no selected target archive.");
					var unitKey = new AdaptationAssetKey(unit.UnitAssetKey.TypeId, unit.UnitAssetKey.FileId);
					var mappings = unit.Adaptation!.Steps
						.Where(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource)
						.Select(step => new AdaptationTargetShellMeshMapping(unitKey, step.SourceMeshInfoIndex ?? throw new InvalidDataException("Replacement step lacks source mesh index."), step.TargetMeshInfoIndex))
						.ToArray();
					return new AdaptationSameKeyTargetShellReconstructionUnit(unitKey, targetArchive.ArchiveId, mappings);
					}).ToArray(),
					preparedEntries.Select(ToAdaptationEntry).ToArray())
				{
					Performance = (stage, unitKey, elapsed) => reporter.Report(
						$"{stage}:{unitKey.FileId:x16}",
						$"{stage switch
						{
							"LoadFacts.SourceUnit" => "读取来源 Unit",
							"LoadFacts.TargetUnit" => "读取 current target Unit",
							"BuildCandidate.Unit" => "构建候选 Unit",
							"ValidateCandidate.Unit" => "验证候选 Unit",
							_ => stage
						}} 0x{unitKey.FileId:x16}，耗时={elapsed.TotalMilliseconds:0}ms",
						0,
						0,
						OperationState.Progress),
					Progress = (stage, completed, total) => reporter.Report(stage, stage switch
					{
						"LoadFacts" => "正在读取 current target 事实",
						"BuildCandidate" => "正在构建候选",
						"WriteCandidate" => "正在写出候选",
						"ValidateCandidate" => "正在验证候选",
						"Finalize" => "正在完成重建",
						_ => "正在处理"
					}, stage == "BuildCandidate" ? buildCandidateProgress.Report(completed, total) : completed, stage == "BuildCandidate" ? buildCandidateProgress.Total : total, OperationState.Progress)
				};
				var execution = await reconstructionOperation.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
				executions.Add((plan, execution.WriteResult));
			}
			reporter.Report("Finalize", "正在完成重建", 0, 1, OperationState.Started);
			var report = await WriteMultiPatchReportAsync(outputDirectory, source, executions, issues, cancellationToken).ConfigureAwait(false);
			reporter.Report("Finalize", "重建报告已完成", 1, 1, OperationState.Progress);
			reporter.Completed("重建已完成");
			return new SameKeyReconstructionOperationResult(true, outputDirectory, report.JsonPath, report.MarkdownPath, executions.Sum(item => item.Plan.SourceUnitCount), executions.Sum(item => item.Plan.Units.Count(unit => unit.Adaptation?.ReplacementCount > 0)), executions.Sum(item => item.Plan.Units.Count(unit => unit.Adaptation?.ReplacementCount == 0)), executions.Sum(item => item.Plan.Units.Sum(unit => unit.Adaptation?.ReplacementCount ?? 0)), executions.Sum(item => item.Plan.Units.Sum(unit => unit.Adaptation?.MinifiedCount ?? 0)), issues);
		}
		catch (OperationCanceledException)
		{
			reporter.Canceled();
			TryDeleteIsolatedOutput(outputDirectory, outputOwnershipMarker, outputCreated, issues, source?.Id ?? default);
			throw;
		}
		catch (Exception exception)
		{
			issues.Add(Error("SameKeyWriteFailed", exception.Message, source?.Id ?? default, exception));
			TryDeleteIsolatedOutput(outputDirectory, outputOwnershipMarker, outputCreated, issues, source?.Id ?? default);
			return Fail(reporter, issues, outputDirectory);
		}
	}

	private static SameKeyReconstructionOperationResult Fail(SameKeyProgressReporter reporter, IReadOnlyList<CoreIssue> issues, string? outputDirectory = null)
	{
		reporter.Failed("重建失败。");
		return Failure(issues, outputDirectory);
	}

	private static void TryDeleteIsolatedOutput(string? outputDirectory, string? ownershipMarker, bool outputCreated, List<CoreIssue> issues, ModNodeId nodeId)
	{
		if (!outputCreated || string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(ownershipMarker)) return;
		try
		{
			if (File.Exists(ownershipMarker) && Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			issues.Add(Error("OutputCleanupFailed", exception.Message, nodeId, exception));
		}
	}

	private sealed class SameKeyProgressReporter
	{
		private readonly IProgress<OperationProgressEvent>? progress;
		private readonly Guid operationId;
		private long sequence;
		public SameKeyProgressReporter(IProgress<OperationProgressEvent>? progress, Guid? operationId)
		{
			this.progress = progress;
			this.operationId = operationId.GetValueOrDefault() is var id && id != Guid.Empty ? id : Guid.NewGuid();
		}
		public void Report(string stageId, string stageText, long completed, long total, OperationState state)
			=> progress?.Report(new OperationProgressEvent(operationId, null, OperationKind.PatchRepair, OperationStage.Processing, state, completed, total, stageText, null, DateTimeOffset.UtcNow, sequence++, stageId, stageText));
		public void Canceled() => progress?.Report(new OperationProgressEvent(operationId, null, OperationKind.PatchRepair, OperationStage.Canceled, OperationState.Canceled, 0, 0, "正在取消", null, DateTimeOffset.UtcNow, sequence++, "Finalize", "正在取消"));
		public void Completed(string message) => progress?.Report(new OperationProgressEvent(operationId, null, OperationKind.PatchRepair, OperationStage.Completed, OperationState.Completed, 1, 1, message, null, DateTimeOffset.UtcNow, sequence++, "Finalize", message));
		public void Failed(string message) => progress?.Report(new OperationProgressEvent(operationId, null, OperationKind.PatchRepair, OperationStage.Failed, OperationState.Failed, 0, 0, message, null, DateTimeOffset.UtcNow, sequence++, "Finalize", message));
	}

	private sealed class BuildCandidateProgress
	{
		private long completed;
		private long currentPatchCompleted;
		public BuildCandidateProgress(int total) => Total = total;
		public long Total { get; }
		public long Report(long patchCompleted, long patchTotal)
		{
			var value = Math.Clamp(patchCompleted, 0, patchTotal);
			if (value == 0)
			{
				currentPatchCompleted = 0;
				return completed;
			}
			completed += Math.Max(0, value - currentPatchCompleted);
			currentPatchCompleted = value;
			return completed;
		}
	}

	private static void CollectPlanIssues(SameKeyReconstructionPlan plan, ModNodeId nodeId, List<CoreIssue> issues)
	{
		issues.AddRange(plan.Issues);
		foreach (var unit in plan.Units)
		{
			issues.AddRange(unit.Issues);
			if (!unit.HasFullTargetShellCoverage) issues.Add(Error("IncompleteTargetShell", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 未覆盖全部 current target mesh。", nodeId));
			if (unit.HasExperimentalCandidate) issues.Add(Error("ExperimentalMeshMapping", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 包含实验性 mesh mapping；正式第一版不会写出。", nodeId));
			if (unit.TargetArchive is null) issues.Add(Error("SelectedArchiveMissing", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 没有可读的 current target archive。", nodeId));
		}
	}

	private async ValueTask<SameKeyReconstructionPlan> CreatePlanAsync(ModNode source, string sourcePatchPath, string gameDataDirectory, string modsRootDirectory, CancellationToken cancellationToken, IProgress<SameKeyPlanningProgress>? planningProgress = null)
	{
		var entries = (await GetPreparedSourceEntriesAsync(source, sourcePatchPath, modsRootDirectory, cancellationToken).ConfigureAwait(false))
			.Select(ToCoreEntry).ToArray();
		if (entries.Length == 0) throw new InvalidOperationException("高级缓存不包含来源 Patch 的 TOC entry 目录；请重新执行高级分析。");
		return await planningService.CreatePlanAsync(new SameKeyReconstructionRequest(sourcePatchPath, gameDataDirectory, PreparedSourceEntries: entries), cancellationToken, planningProgress).ConfigureAwait(false);
	}

	private async ValueTask<IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>> GetPreparedSourceEntriesAsync(ModNode source, string sourcePatchPath, string modsRootDirectory, CancellationToken cancellationToken)
		=> (await advancedAnalysis.GetRequiredAnalysesAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false))
			.Where(analysis => string.Equals(Path.GetFullPath(analysis.Input.PatchTocFilePath), Path.GetFullPath(sourcePatchPath), StringComparison.OrdinalIgnoreCase))
			.SelectMany(analysis => analysis.Entries).ToArray();

	private static async ValueTask<(string JsonPath, string MarkdownPath)> WriteReportAsync(string outputDirectory, ModNode source, ModSameKeyReconstructionState state, HD2ModAdaptation.PatchReconstruction.PatchArchiveFileWriteResult write, CancellationToken cancellationToken)
	{
		var report = new
		{
			SourceMod = source.Metadata.Name,
			SourcePatch = state.SourcePatchTocPath,
			GeneratedUtc = DateTimeOffset.UtcNow,
			InternalStructuralChecks = "Passed",
			ExternalValidation = "Pending: Blender or in-game validation is required.",
			Output = new { write.TocFilePath, write.StreamFilePath, write.GpuResourceFilePath, TocSha256 = await HashFileAsync(write.TocFilePath, cancellationToken).ConfigureAwait(false), StreamSha256 = await HashFileAsync(write.StreamFilePath, cancellationToken).ConfigureAwait(false), GpuSha256 = await HashFileAsync(write.GpuResourceFilePath, cancellationToken).ConfigureAwait(false) },
			Units = state.Plan!.Units.Select(unit => new { AssetKey = $"0x{unit.UnitAssetKey.FileId:x16}", Archive = unit.TargetArchive?.ArchiveId, ReplacementMeshes = unit.Adaptation?.ReplacementCount ?? 0, MinifiedMeshes = unit.Adaptation?.MinifiedCount ?? 0, SharedTarget = unit.IsSharedTarget }),
			Issues = state.Issues.Select(issue => new { Severity = issue.Severity.ToString(), issue.Code, issue.Message })
		};
		var jsonPath = Path.Combine(outputDirectory, "reconstruction-report.json");
		var markdownPath = Path.Combine(outputDirectory, "reconstruction-report.md");
		await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
		var markdown = new StringBuilder()
			.AppendLine("# Same-AssetKey reconstruction report")
			.AppendLine()
			.AppendLine($"- Source Mod: {source.Metadata.Name}")
			.AppendLine($"- Source Patch: {state.SourcePatchTocPath}")
			.AppendLine($"- Output Patch: {write.TocFilePath}")
			.AppendLine("- Non-Unit resources: retained unchanged from the input Patch")
			.AppendLine($"- Units: {state.Plan!.Units.Count}")
			.AppendLine("- Internal structural checks: passed")
			.AppendLine("- External validation: pending Blender or in-game test")
			.AppendLine()
			.AppendLine("## Units");
		foreach (var unit in state.Plan.Units) markdown.AppendLine($"- 0x{unit.UnitAssetKey.FileId:x16}: {unit.TargetArchive?.ArchiveId}; replacement {unit.Adaptation?.ReplacementCount ?? 0}; minify {unit.Adaptation?.MinifiedCount ?? 0}");
		if (state.Issues.Count != 0)
		{
			markdown.AppendLine().AppendLine("## Notices");
			foreach (var issue in state.Issues) markdown.AppendLine($"- {issue.Severity}: {issue.Code} — {issue.Message}");
		}
		await File.WriteAllTextAsync(markdownPath, markdown.ToString(), cancellationToken).ConfigureAwait(false);
		return (jsonPath, markdownPath);
	}

	private static async ValueTask<(string JsonPath, string MarkdownPath)> WriteMultiPatchReportAsync(
		string outputDirectory,
		ModNode source,
		IReadOnlyList<(SameKeyReconstructionPlan Plan, HD2ModAdaptation.PatchReconstruction.PatchArchiveFileWriteResult Write)> executions,
		IReadOnlyList<CoreIssue> issues,
		CancellationToken cancellationToken)
	{
		var outputs = new List<object>();
		foreach (var (plan, write) in executions)
		{
			outputs.Add(new
			{
				SourcePatch = plan.Request.SourcePatchTocPath,
				Output = new { write.TocFilePath, write.StreamFilePath, write.GpuResourceFilePath, TocSha256 = await HashFileAsync(write.TocFilePath, cancellationToken).ConfigureAwait(false) },
				Units = plan.Units.Select(unit => new { AssetKey = $"0x{unit.UnitAssetKey.FileId:x16}", Archive = unit.TargetArchive?.ArchiveId, ReplacementMeshes = unit.Adaptation?.ReplacementCount ?? 0, MinifiedMeshes = unit.Adaptation?.MinifiedCount ?? 0 }).ToArray()
			});
		}
		var jsonPath = Path.Combine(outputDirectory, "reconstruction-report.json");
		var markdownPath = Path.Combine(outputDirectory, "reconstruction-report.md");
		await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new
		{
			SourceMod = source.Metadata.Name,
			GeneratedUtc = DateTimeOffset.UtcNow,
			PatchCount = outputs.Count,
			Outputs = outputs,
			Issues = issues.Select(issue => new { Severity = issue.Severity.ToString(), issue.Code, issue.Message })
		}, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
		var markdown = new StringBuilder()
			.AppendLine("# Same-AssetKey reconstruction report")
			.AppendLine()
			.AppendLine($"- Source Mod: {source.Metadata.Name}")
			.AppendLine($"- Rebuilt Patch groups: {executions.Count}")
			.AppendLine("- Each source Patch group is rebuilt into its own output Patch group; Patch shells are not merged.")
			.AppendLine()
			.AppendLine("## Outputs");
		foreach (var (plan, write) in executions)
		{
			markdown.AppendLine($"- {Path.GetFileName(plan.Request.SourcePatchTocPath)} -> {Path.GetFileName(write.TocFilePath)}; Units {plan.SourceUnitCount}; replacement {plan.Units.Sum(unit => unit.Adaptation?.ReplacementCount ?? 0)}; minify {plan.Units.Sum(unit => unit.Adaptation?.MinifiedCount ?? 0)}");
		}
		if (issues.Count != 0)
		{
			markdown.AppendLine().AppendLine("## Notices");
			foreach (var issue in issues) markdown.AppendLine($"- {issue.Severity}: {issue.Code} — {issue.Message}");
		}
		await File.WriteAllTextAsync(markdownPath, markdown.ToString(), cancellationToken).ConfigureAwait(false);
		return (jsonPath, markdownPath);
	}

	private static async ValueTask WriteFormalValidationChecklistAsync(string outputDirectory, ModNode source, ModSameKeyReconstructionState state, CancellationToken cancellationToken)
	{
		var checklist = new StringBuilder()
			.AppendLine("# Current-target reconstruction formal validation checklist")
			.AppendLine()
			.AppendLine($"- Candidate: {source.Metadata.Name}")
			.AppendLine($"- Generated (UTC): {DateTimeOffset.UtcNow:O}")
			.AppendLine($"- Units: {state.Plan?.SourceUnitCount ?? 0}; replacement meshes: {state.ReplacementMeshCount}; minified meshes: {state.MinifiedMeshCount}")
			.AppendLine("- Non-Unit resources and sidecars: retained from the input Patch")
			.AppendLine()
			.AppendLine("## Candidate guarantees")
			.AppendLine()
			.AppendLine("- This output is generated from current same-AssetKey target Units; it does not reuse old source Unit or Composite Unit payloads.")
			.AppendLine("- Every current target mesh slot is covered by either a transferred mesh or a minified target-shell mesh.")
			.AppendLine("- Material bindings in rebuilt source meshes and retained non-Unit Patch entries are not repackaged or validated by this operation.")
			.AppendLine("- Experimental mesh mappings are blocked before this candidate can be written.")
			.AppendLine("- Bone weights are conservatively re-encoded from the source data. SDK byte-for-byte weight equality is not required for this validation candidate.")
			.AppendLine()
			.AppendLine("## Before deployment")
			.AppendLine()
			.AppendLine("- Keep the source Mod unchanged and keep this candidate as a separate Mod entry.")
			.AppendLine("- Enable only this candidate for the covered assets; disable the source Mod to avoid same-AssetKey override ambiguity.")
			.AppendLine("- Record the game build and the candidate output directory used for the test.")
			.AppendLine()
			.AppendLine("## In-game acceptance")
			.AppendLine()
			.AppendLine("- Verify armoury loading, equipment preview, and a mission load without crashes or missing-resource errors.")
			.AppendLine("- Check idle, walking, sprinting, aiming, crouching, diving, ragdoll/recovery, and camera-distance changes.")
			.AppendLine("- Check first- and third-person views for visible stretching, detached geometry, severe clipping, or original target-shell remnants.")
			.AppendLine("- Check material appearance separately; this operation preserves existing material references and does not package dependencies.")
			.AppendLine()
			.AppendLine("## Result classification")
			.AppendLine()
			.AppendLine("- Pass: no crash, no loading failure, and no reproducible visible geometry or skinning defect.")
			.AppendLine("- Investigate: missing mesh, wrong material, persistent original shell, visible deformation, or a game error. Preserve this directory and reconstruction-report.json for diagnosis.");
		await File.WriteAllTextAsync(Path.Combine(outputDirectory, "formal-validation-checklist.md"), checklist.ToString(), cancellationToken).ConfigureAwait(false);
	}

	private static async ValueTask<string?> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path)) return null;
		await using var stream = File.OpenRead(path);
		return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
	}

	private IReadOnlyList<string> FindBasePatchPaths(ModNode node, string modsRootDirectory)
	{
		var directory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(directory)) return Array.Empty<string>();
		return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Where(path => fileNameParser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static ModSameKeyReconstructionState CreateState(ModNodeId sourceId, string? patchPath, SameKeyReconstructionPlan? plan, bool indexCurrent, IReadOnlyList<CoreIssue> issues)
		=> new(sourceId, patchPath, plan, indexCurrent, plan?.Units.Count(unit => unit.Adaptation?.ReplacementCount > 0) ?? 0, plan?.Units.Count(unit => unit.Adaptation?.ReplacementCount == 0) ?? 0, plan?.Units.Sum(unit => unit.Adaptation?.ReplacementCount ?? 0) ?? 0, plan?.Units.Sum(unit => unit.Adaptation?.MinifiedCount ?? 0) ?? 0, plan?.Units.Count(unit => unit.IsSharedTarget) ?? 0, issues);

	private static SameKeyReconstructionOperationResult Failure(IReadOnlyList<CoreIssue> issues, string? outputDirectory = null)
		=> new(false, outputDirectory, null, null, 0, 0, 0, 0, 0, issues);

	private static CoreIssue Error(string code, string message, ModNodeId nodeId, Exception? exception = null)
		=> new(CoreIssueSeverity.Error, code, message, NodeId: nodeId, ExceptionMessage: exception?.ToString());

	private static PatchTocEntry ToCoreEntry(HD2ModAdaptation.PatchReconstruction.PatchTocEntry entry)
		=> new(new AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), entry.SourceFilePath, entry.SourceFileName,
			entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset, entry.Unknown1, entry.Unknown2,
			entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.Unknown3, entry.Unknown4, entry.EntryIndex);

	private static HD2ModAdaptation.PatchReconstruction.PatchTocEntry ToAdaptationEntry(HD2ModAdaptation.PatchReconstruction.PatchTocEntry entry) => entry;

	private static (string Directory, string OwnershipMarker) CreateOwnedOutputDirectory(string root, string sourceName)
	{
		var fullRoot = Path.GetFullPath(root);
		Directory.CreateDirectory(fullRoot);
		for (var attempt = 0; attempt < 8; attempt++)
		{
			var directory = Path.Combine(fullRoot, $"{Sanitize(sourceName)}-same-key-rebuilt-{Guid.NewGuid():N}");
			try
			{
				Directory.CreateDirectory(directory);
				var marker = Path.Combine(directory, ".same-key-owner");
				using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
				return (directory, marker);
			}
			catch (IOException) when (Directory.Exists(directory))
			{
				// A GUID collision or marker race is never grounds for deleting the directory.
			}
		}
		throw new IOException("无法原子创建独占的 same-key 输出目录。");
	}

	private static string Sanitize(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
}
