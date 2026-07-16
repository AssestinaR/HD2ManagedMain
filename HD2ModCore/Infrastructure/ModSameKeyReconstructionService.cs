using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using AdaptationGameDataUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMeshReader;
using AdaptationPatchArchiveWriter = HD2ModAdaptation.PatchReconstruction.PatchArchiveWriter;
using AdaptationPatchEntryPayloadReader = HD2ModAdaptation.PatchReconstruction.PatchEntryPayloadReader;
using AdaptationPatchTocEntry = HD2ModAdaptation.PatchReconstruction.PatchTocEntry;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationPatchUnitMesh = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMesh;
using AdaptationPatchUnitMeshEditResult = HD2ModAdaptation.PatchReconstruction.PatchUnitMeshEditResult;
using AdaptationPatchUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMeshReader;
using AdaptationSdkStyleTargetShellPatchOutput = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput;
using AdaptationSdkStyleTargetShellPatchOutputBuilder = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutputBuilder;
using AdaptationSdkStyleTargetShellPatchWorkItem = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchWorkItem;
using AdaptationTargetShellMeshMapping = HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellMeshMapping;

namespace HD2ModCore.Infrastructure;

// Purpose: Orchestrates a fully current-target same-key reconstruction without permitting the UI to parse or write patch binaries.
public sealed class ModSameKeyReconstructionService : IModSameKeyReconstructionService
{
	private readonly IPatchFileNameParser fileNameParser;
	private readonly ISameKeyReconstructionPlanningService planningService;
	private readonly IAssetArchiveIndexService assetIndex;
	private readonly IArchiveHashesProvider archiveHashes;
	private readonly AdaptationSdkStyleTargetShellPatchOutputBuilder outputBuilder;
	private readonly AdaptationPatchArchiveWriter archiveWriter;

	public ModSameKeyReconstructionService(
		IPatchFileNameParser fileNameParser,
		ISameKeyReconstructionPlanningService planningService,
		IAssetArchiveIndexService assetIndex,
		IArchiveHashesProvider archiveHashes,
		AdaptationSdkStyleTargetShellPatchOutputBuilder? outputBuilder = null,
		AdaptationPatchArchiveWriter? archiveWriter = null)
	{
		this.fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		this.planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
		this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex));
		this.archiveHashes = archiveHashes ?? throw new ArgumentNullException(nameof(archiveHashes));
		this.outputBuilder = outputBuilder ?? new AdaptationSdkStyleTargetShellPatchOutputBuilder();
		this.archiveWriter = archiveWriter ?? new AdaptationPatchArchiveWriter();
	}

	public async ValueTask<ModSameKeyReconstructionState> InspectAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		var issues = new List<CoreIssue>();
		var patchPaths = FindBasePatchPaths(source, modsRootDirectory);
		if (patchPaths.Count != 1)
		{
			issues.Add(Error("SinglePatchRequired", patchPaths.Count == 0 ? "Mod 没有 Patch 主文件。" : "当前重建仅支持只含一个 Patch 文件组的 Mod。", source.Id));
			return CreateState(source.Id, null, null, false, issues);
		}
		if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
		{
			issues.Add(Error("GameDataMissing", "请先在设置中配置有效的 Game Data 文件夹。", source.Id));
			return CreateState(source.Id, patchPaths[0], null, false, issues);
		}

		GameDataIndexStatus indexStatus;
		try
		{
			indexStatus = await assetIndex.GetIndexStatusAsync(gameDataDirectory, await archiveHashes.GetArchiveHashesJsonAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			issues.Add(Error("GameDataIndexUnreadable", exception.Message, source.Id, exception));
			return CreateState(source.Id, patchPaths[0], null, false, issues);
		}
		if (!indexStatus.IsCurrent)
		{
			issues.Add(Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期；请先在状态页建立/重建资产索引。", source.Id));
			return CreateState(source.Id, patchPaths[0], null, false, issues);
		}

		try
		{
			var plan = await planningService.CreatePlanAsync(new SameKeyReconstructionRequest(patchPaths[0], gameDataDirectory), cancellationToken).ConfigureAwait(false);
			issues.AddRange(plan.Issues);
			foreach (var unit in plan.Units)
			{
				issues.AddRange(unit.Issues);
				if (!unit.HasFullTargetShellCoverage) issues.Add(Error("IncompleteTargetShell", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 未覆盖全部 current target mesh。", source.Id));
				if (unit.HasExperimentalCandidate) issues.Add(Error("ExperimentalMeshMapping", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 包含实验性 mesh mapping；正式第一版不会写出。", source.Id));
				if (unit.TargetArchive is null) issues.Add(Error("SelectedArchiveMissing", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 没有可读的 current target archive。", source.Id));
			}
			if (plan.Units.Count != 0 && plan.Units.Any(unit => unit.Adaptation?.ReplacementCount == 0))
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "MinifyOnlyTargetUnits", "部分 Unit 没有 replacement mesh；它们将作为完整 minify-only current target shell 输出。", NodeId: source.Id));
			}
			return CreateState(source.Id, patchPaths[0], plan, true, issues);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			issues.Add(Error("SameKeyPlanFailed", exception.Message, source.Id, exception));
			return CreateState(source.Id, patchPaths[0], null, true, issues);
		}
	}

	public async ValueTask<SameKeyReconstructionOperationResult> WriteTestCopyAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		string outputRootDirectory,
		CancellationToken cancellationToken = default)
		=> await GenerateCandidateAsync(source, modsRootDirectory, gameDataDirectory, outputRootDirectory, cancellationToken).ConfigureAwait(false);

	public async ValueTask<SameKeyReconstructionOperationResult> GenerateCandidateAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		string outputRootDirectory,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(outputRootDirectory)) return Failure(new[] { Error("OutputDirectoryMissing", "必须选择输出文件夹。", source.Id) });
		var issues = new List<CoreIssue>();
		var patchPaths = FindBasePatchPaths(source, modsRootDirectory);
		if (patchPaths.Count != 1)
		{
			issues.Add(Error("SinglePatchRequired", patchPaths.Count == 0 ? "Mod 没有 Patch 主文件。" : "当前重建仅支持只含一个 Patch 文件组的 Mod。", source.Id));
			return Failure(issues);
		}
		if (string.IsNullOrWhiteSpace(gameDataDirectory) || !Directory.Exists(gameDataDirectory))
		{
			issues.Add(Error("GameDataMissing", "请先在设置中配置有效的 Game Data 文件夹。", source.Id));
			return Failure(issues);
		}

		GameDataIndexStatus indexStatus;
		try
		{
			indexStatus = await assetIndex.GetIndexStatusAsync(gameDataDirectory, await archiveHashes.GetArchiveHashesJsonAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			issues.Add(Error("GameDataIndexUnreadable", exception.Message, source.Id, exception));
			return Failure(issues);
		}
		if (!indexStatus.IsCurrent)
		{
			issues.Add(Error("GameDataIndexNotCurrent", "Game Data 资产索引不可用或已过期；请先在状态页建立/重建资产索引。", source.Id));
			return Failure(issues);
		}

		SameKeyReconstructionPlan plan;
		try
		{
			plan = await planningService.CreatePlanAsync(new SameKeyReconstructionRequest(patchPaths[0], gameDataDirectory), cancellationToken).ConfigureAwait(false);
			issues.AddRange(plan.Issues);
			foreach (var unit in plan.Units)
			{
				issues.AddRange(unit.Issues);
				if (!unit.HasFullTargetShellCoverage) issues.Add(Error("IncompleteTargetShell", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 未覆盖全部 current target mesh。", source.Id));
				if (unit.HasExperimentalCandidate) issues.Add(Error("ExperimentalMeshMapping", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 包含实验性 mesh mapping；正式第一版不会写出。", source.Id));
				if (unit.TargetArchive is null) issues.Add(Error("SelectedArchiveMissing", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 没有可读的 current target archive。", source.Id));
			}
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			issues.Add(Error("SameKeyPlanFailed", exception.Message, source.Id, exception));
			return Failure(issues);
		}
		if (plan.SourceUnitCount == 0 || issues.Any(issue => issue.Severity == CoreIssueSeverity.Error)) return Failure(issues);

		var sourcePath = patchPaths[0];
		var state = CreateState(source.Id, sourcePath, plan, true, issues);
		var outputDirectory = CreateOutputDirectory(outputRootDirectory, source.Metadata.Name);
		try
		{
			Directory.CreateDirectory(outputDirectory);
			var sourceEntries = await new AdaptationPatchTocScanner().ScanEntriesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
			var sourceUnits = new Dictionary<AdaptationAssetKey, AdaptationPatchUnitMesh>();
			foreach (var entry in sourceEntries.Where(entry => entry.AssetKey.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId))
			{
				sourceUnits.Add(entry.AssetKey, await new AdaptationPatchUnitMeshReader().ReadAsync(entry, sourceEntries, cancellationToken: cancellationToken).ConfigureAwait(false));
			}
			var expectedSourceKeys = plan.Units.Select(unit => new AdaptationAssetKey(unit.UnitAssetKey.TypeId, unit.UnitAssetKey.FileId)).ToHashSet();
			if (sourceUnits.Count != plan.Units.Count || !sourceUnits.Keys.ToHashSet().SetEquals(expectedSourceKeys))
			{
				throw new InvalidDataException("Source patch Unit 集合已变化；请重新创建重建计划。输出未写入。");
			}

			var resolver = new AdaptationGameDataPackageResolver(gameDataDirectory);
			var targetReader = new AdaptationGameDataUnitMeshReader(resolver);
			var workItems = new List<AdaptationSdkStyleTargetShellPatchWorkItem>();
			foreach (var unit in plan.Units)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var targetArchive = unit.TargetArchive ?? throw new InvalidDataException($"Unit 0x{unit.UnitAssetKey.FileId:x16} has no selected target archive.");
				var unitKey = new AdaptationAssetKey(unit.UnitAssetKey.TypeId, unit.UnitAssetKey.FileId);
				var target = await targetReader.ReadAsync(targetArchive.ArchiveId, unitKey, allowGlobalDependencySearch: true, cancellationToken: cancellationToken).ConfigureAwait(false);
				var mappings = unit.Adaptation!.Steps
					.Where(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource)
					.Select(step => new AdaptationTargetShellMeshMapping(unitKey, step.SourceMeshInfoIndex ?? throw new InvalidDataException("Replacement step lacks source mesh index."), step.TargetMeshInfoIndex))
					.ToArray();
				workItems.Add(new AdaptationSdkStyleTargetShellPatchWorkItem(target, new[] { sourceUnits[unitKey] }, mappings));
			}
			var output = outputBuilder.Build(workItems);
			if (!output.ReplacedSourceUnitAssetKeys.ToHashSet().SetEquals(sourceUnits.Keys))
			{
				throw new InvalidDataException("Reconstruction must replace every old source Unit; refusing to preserve obsolete Unit data.");
			}
			var modelDirectory = Path.Combine(outputDirectory, "model");
			var removals = await GetAllSourceUnitAndCompositeRemovalsAsync(sourceEntries, cancellationToken).ConfigureAwait(false);
			var headerArchive = plan.Units.First().TargetArchive ?? throw new InvalidDataException("No selected current target archive is available for the output header.");
			var headerTemplate = await resolver.GetPackageTocAsync(headerArchive.ArchiveId, cancellationToken).ConfigureAwait(false)
				?? throw new FileNotFoundException("The selected current target archive TOC could not be read.", headerArchive.ArchiveId);
			var write = await archiveWriter.WriteAsync(sourcePath, modelDirectory, Array.Empty<AdaptationPatchUnitMeshEditResult>(), output.AdditionalEntries, removals, preserveOriginalStream: false, headerTemplateTocData: headerTemplate.Data, cancellationToken: cancellationToken).ConfigureAwait(false);
			var verification = await VerifyOutputAsync(write.TocFilePath, output, cancellationToken).ConfigureAwait(false);
			if (verification.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, verification));
			var report = await WriteReportAsync(outputDirectory, source, state, write, cancellationToken).ConfigureAwait(false);
			await WriteFormalValidationChecklistAsync(outputDirectory, source, state, cancellationToken).ConfigureAwait(false);
			return new SameKeyReconstructionOperationResult(true, outputDirectory, modelDirectory, report.JsonPath, report.MarkdownPath, output.UnitResults.Count, output.UnitResults.Count(result => result.ReplacementCount > 0), output.UnitResults.Count(result => result.ReplacementCount == 0), output.UnitResults.Sum(result => result.ReplacementCount), output.UnitResults.Sum(result => result.MinifiedCount), state.Issues);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			return Failure(state.Issues.Concat(new[] { Error("SameKeyWriteFailed", exception.Message, source.Id, exception) }).ToArray(), outputDirectory);
		}
	}

	private async ValueTask<IReadOnlyList<AdaptationPatchTocEntry>> GetAllSourceUnitAndCompositeRemovalsAsync(IReadOnlyList<AdaptationPatchTocEntry> sourceEntries, CancellationToken cancellationToken)
	{
		const ulong compositeUnitTypeId = 0xc4f0f4be7fb0c8d6;
		var unitEntries = sourceEntries.Where(entry => entry.AssetKey.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId).ToArray();
		var compositeIds = new HashSet<ulong>();
		foreach (var unit in unitEntries)
		{
			var payload = await new AdaptationPatchEntryPayloadReader().ReadPayloadAsync(unit, cancellationToken).ConfigureAwait(false);
			if (payload.TocData.Length >= 24)
			{
				var compositeId = BitConverter.ToUInt64(payload.TocData, 16);
				if (compositeId != 0) compositeIds.Add(compositeId);
			}
		}
		return unitEntries.Concat(sourceEntries.Where(entry => entry.AssetKey.TypeId == compositeUnitTypeId && compositeIds.Contains(entry.AssetKey.FileId))).ToArray();
	}

	private async ValueTask<IReadOnlyList<string>> VerifyOutputAsync(string outputTocPath, AdaptationSdkStyleTargetShellPatchOutput output, CancellationToken cancellationToken)
	{
		const ulong compositeUnitTypeId = 0xc4f0f4be7fb0c8d6;
		var errors = new List<string>();
		var entries = await new AdaptationPatchTocScanner().ScanEntriesAsync(outputTocPath, cancellationToken).ConfigureAwait(false);
		var unitKeys = entries.Where(entry => entry.AssetKey.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
		if (!unitKeys.SetEquals(output.UnitResults.Select(result => result.TargetUnitAssetKey))) errors.Add("输出 Unit 集合与批准的 current target Unit 集合不一致。");
		if (entries.Any(entry => entry.AssetKey.TypeId == compositeUnitTypeId)) errors.Add("输出仍包含 Composite Unit；阶段 1 的 current target shell 不应保留旧 Composite。");
		if (entries.GroupBy(entry => entry.AssetKey).Any(group => group.Count() != 1)) errors.Add("输出包含重复 AssetKey。");
		foreach (var unit in output.UnitResults)
		{
			var entry = entries.SingleOrDefault(candidate => candidate.AssetKey == unit.TargetUnitAssetKey);
			if (entry is null) { errors.Add($"输出缺少 Unit 0x{unit.TargetUnitAssetKey.FileId:x16}。"); continue; }
			var readback = await new AdaptationPatchUnitMeshReader().ReadAsync(entry, entries, cancellationToken: cancellationToken).ConfigureAwait(false);
			if (readback.Model.RawMeshData.Count != unit.CoveredTargetMeshCount) errors.Add($"Unit 0x{unit.TargetUnitAssetKey.FileId:x16} readback mesh coverage differs from the rebuilt target shell.");
			foreach (var boneIndex in unit.RebuiltBoneInfoIndexes)
			{
				if (boneIndex < 0 || boneIndex >= readback.Model.BoneInfos.Count || boneIndex >= unit.BoneInfos.Count) { errors.Add($"Unit 0x{unit.TargetUnitAssetKey.FileId:x16} has an invalid rebuilt BoneInfo index."); continue; }
				var expected = unit.BoneInfos[boneIndex];
				var actual = readback.Model.BoneInfos[boneIndex];
				if (!expected.RealIndices.SequenceEqual(actual.RealIndices) || !expected.BoneMatrices.SelectMany(matrix => matrix).SequenceEqual(actual.BoneMatrices.SelectMany(matrix => matrix)) || !expected.Remaps.SelectMany(remap => remap.FakeIndices).SequenceEqual(actual.Remaps.SelectMany(remap => remap.FakeIndices))) errors.Add($"Unit 0x{unit.TargetUnitAssetKey.FileId:x16} BoneInfo {boneIndex} failed readback verification.");
			}
		}
		return errors;
	}

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

	private static async ValueTask WriteFormalValidationChecklistAsync(string outputDirectory, ModNode source, ModSameKeyReconstructionState state, CancellationToken cancellationToken)
	{
		var checklist = new StringBuilder()
			.AppendLine("# Current-target reconstruction formal validation checklist")
			.AppendLine()
			.AppendLine($"- Candidate: {source.Metadata.Name}")
			.AppendLine($"- Generated (UTC): {DateTimeOffset.UtcNow:O}")
			.AppendLine($"- Units: {state.Plan?.SourceUnitCount ?? 0}; replacement meshes: {state.ReplacementMeshCount}; minified meshes: {state.MinifiedMeshCount}")
			.AppendLine()
			.AppendLine("## Candidate guarantees")
			.AppendLine()
			.AppendLine("- This output is generated from current same-AssetKey target Units; it does not reuse old source Unit or Composite Unit payloads.")
			.AppendLine("- Every current target mesh slot is covered by either a transferred mesh or a minified target-shell mesh.")
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
			.AppendLine("- Check material appearance separately. A current-target material fallback can be expected when a source material/texture dependency closure is unavailable.")
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
		=> new(false, outputDirectory, null, null, null, 0, 0, 0, 0, 0, issues);

	private static CoreIssue Error(string code, string message, ModNodeId nodeId, Exception? exception = null)
		=> new(CoreIssueSeverity.Error, code, message, NodeId: nodeId, ExceptionMessage: exception?.ToString());

	private static string CreateOutputDirectory(string root, string sourceName)
		=> Path.Combine(Path.GetFullPath(root), $"{Sanitize(sourceName)}-same-key-rebuilt-{DateTime.Now:yyyyMMdd-HHmmss}");

	private static string Sanitize(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
}
