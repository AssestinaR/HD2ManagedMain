using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationSameKeyTargetShellReconstructionOperation = HD2ModAdaptation.PatchReconstruction.UnitMesh.SameKeyTargetShellReconstructionOperation;
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
	private readonly AdaptationSameKeyTargetShellReconstructionOperation reconstructionOperation;

	public ModSameKeyReconstructionService(
		IPatchFileNameParser fileNameParser,
		ISameKeyReconstructionPlanningService planningService,
		IAssetArchiveIndexService assetIndex,
		IArchiveHashesProvider archiveHashes,
		IAdvancedModAnalysisService advancedAnalysis,
		AdaptationSameKeyTargetShellReconstructionOperation? reconstructionOperation = null)
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
			var plan = await CreatePlanAsync(source, patchPaths[0], gameDataDirectory, modsRootDirectory, cancellationToken).ConfigureAwait(false);
			issues.AddRange(plan.Issues);
			foreach (var unit in plan.Units)
			{
				issues.AddRange(unit.Issues);
				if (!unit.HasFullTargetShellCoverage) issues.Add(Error("IncompleteTargetShell", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 未覆盖全部 current target mesh。", source.Id));
				if (unit.HasExperimentalCandidate) issues.Add(Error("ExperimentalMeshMapping", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 包含实验性 mesh mapping；正式第一版不会写出。", source.Id));
				if (unit.TargetArchive is null) issues.Add(Error("SelectedArchiveMissing", $"Unit 0x{unit.UnitAssetKey.FileId:x16} 没有可读的 current target archive。", source.Id));
			}
			return CreateState(source.Id, patchPaths[0], plan, true, issues);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			issues.Add(Error("SameKeyPlanFailed", exception.Message, source.Id, exception));
			return CreateState(source.Id, patchPaths[0], null, true, issues);
		}
	}

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
			plan = await CreatePlanAsync(source, patchPaths[0], gameDataDirectory, modsRootDirectory, cancellationToken).ConfigureAwait(false);
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
			preparedEntries.Select(ToAdaptationEntry).ToArray());
			var execution = await reconstructionOperation.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
			var report = await WriteReportAsync(outputDirectory, source, state, execution.WriteResult, cancellationToken).ConfigureAwait(false);
			await WriteFormalValidationChecklistAsync(outputDirectory, source, state, cancellationToken).ConfigureAwait(false);
			return new SameKeyReconstructionOperationResult(true, outputDirectory, report.JsonPath, report.MarkdownPath, execution.UnitCount, execution.UnitsWithReplacements, execution.MinifyOnlyUnitCount, execution.ReplacementMeshCount, execution.MinifiedMeshCount, state.Issues);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			return Failure(state.Issues.Concat(new[] { Error("SameKeyWriteFailed", exception.Message, source.Id, exception) }).ToArray(), outputDirectory);
		}
	}

	private async ValueTask<SameKeyReconstructionPlan> CreatePlanAsync(ModNode source, string sourcePatchPath, string gameDataDirectory, string modsRootDirectory, CancellationToken cancellationToken)
	{
		var entries = (await GetPreparedSourceEntriesAsync(source, sourcePatchPath, modsRootDirectory, cancellationToken).ConfigureAwait(false))
			.Select(ToCoreEntry).ToArray();
		if (entries.Length == 0) throw new InvalidOperationException("高级缓存不包含来源 Patch 的 TOC entry 目录；请重新执行高级分析。");
		return await planningService.CreatePlanAsync(new SameKeyReconstructionRequest(sourcePatchPath, gameDataDirectory, PreparedSourceEntries: entries), cancellationToken).ConfigureAwait(false);
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

	private static string CreateOutputDirectory(string root, string sourceName)
		=> Path.Combine(Path.GetFullPath(root), $"{Sanitize(sourceName)}-same-key-rebuilt-{DateTime.Now:yyyyMMdd-HHmmss}");

	private static string Sanitize(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
}
