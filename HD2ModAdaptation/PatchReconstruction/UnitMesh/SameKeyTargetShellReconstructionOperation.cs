using System.Diagnostics;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Executes an approved same-key target-shell reconstruction entirely within the Patch/Unit binary adaptation layer.
namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

public interface ISameKeyTargetShellReconstructionOperation
{
	ValueTask<SameKeyTargetShellReconstructionResult> ExecuteAsync(
		SameKeyTargetShellReconstructionRequest request,
		CancellationToken cancellationToken = default);
}

public sealed class SameKeyTargetShellReconstructionOperation : ISameKeyTargetShellReconstructionOperation
{
	// Purpose: Reports coarse same-key reconstruction boundaries without exposing payload-level activity.
	public const string InspectEligibilityStageId = "InspectEligibility";
	public const string LoadFactsStageId = "LoadFacts";
	public const string PlanStageId = "Plan";
	public const string BuildCandidateStageId = "BuildCandidate";
	public const string WriteCandidateStageId = "WriteCandidate";
	public const string ValidateCandidateStageId = "ValidateCandidate";
	public const string FinalizeStageId = "Finalize";
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;
	private readonly PatchTocScanner scanner;
	private readonly PatchUnitMeshReader unitReader;
	private readonly PatchEntryPayloadReader payloadReader;
	private readonly PatchArchiveWriter archiveWriter;
	private readonly SdkStyleTargetShellPatchOutputBuilder outputBuilder;

	public SameKeyTargetShellReconstructionOperation(
		PatchTocScanner? scanner = null,
		PatchUnitMeshReader? unitReader = null,
		PatchEntryPayloadReader? payloadReader = null,
		PatchArchiveWriter? archiveWriter = null,
		SdkStyleTargetShellPatchOutputBuilder? outputBuilder = null)
	{
		this.scanner = scanner ?? new PatchTocScanner();
		this.unitReader = unitReader ?? new PatchUnitMeshReader();
		this.payloadReader = payloadReader ?? new PatchEntryPayloadReader();
		this.archiveWriter = archiveWriter ?? new PatchArchiveWriter(this.scanner, this.payloadReader);
		this.outputBuilder = outputBuilder ?? new SdkStyleTargetShellPatchOutputBuilder(
			new SdkStyleTargetShellUnitReconstructor(planCanonicalSkinningLayout: true));
	}

	public async ValueTask<SameKeyTargetShellReconstructionResult> ExecuteAsync(
		SameKeyTargetShellReconstructionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		request.Validate();
		request.Progress?.Invoke(LoadFactsStageId, 0, request.Units.Count);
		var sourceEntries = request.PreparedSourceEntries is { Count: > 0 }
			? request.PreparedSourceEntries
			: await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
		var expectedSourceKeys = request.Units.Select(unit => unit.UnitAssetKey).ToHashSet();
		var sourceUnits = new Dictionary<AssetKey, PatchUnitMesh>();
		foreach (var entry in sourceEntries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var stopwatch = Stopwatch.StartNew();
			sourceUnits.Add(entry.AssetKey, await unitReader.ReadAsync(entry, sourceEntries, cancellationToken: cancellationToken).ConfigureAwait(false));
			request.Performance?.Invoke("LoadFacts.SourceUnit", entry.AssetKey, stopwatch.Elapsed);
		}
		if (sourceUnits.Count != request.Units.Count || !sourceUnits.Keys.ToHashSet().SetEquals(expectedSourceKeys))
		{
			throw new InvalidDataException("Source patch Unit 集合已变化；请重新创建重建计划。输出未写入。");
		}

		var resolver = new GameDataPackageResolver(request.GameDataDirectory);
		var targetReader = new GameDataUnitMeshReader(resolver);
		var workItems = new List<SdkStyleTargetShellPatchWorkItem>(request.Units.Count);
		foreach (var unit in request.Units)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var stopwatch = Stopwatch.StartNew();
			var target = await targetReader.ReadAsync(unit.TargetArchiveId, unit.UnitAssetKey, allowGlobalDependencySearch: true, cancellationToken: cancellationToken).ConfigureAwait(false);
			workItems.Add(new SdkStyleTargetShellPatchWorkItem(target, new[] { sourceUnits[unit.UnitAssetKey] }, unit.MeshMappings));
			request.Performance?.Invoke("LoadFacts.TargetUnit", unit.UnitAssetKey, stopwatch.Elapsed);
			request.Progress?.Invoke(LoadFactsStageId, workItems.Count, request.Units.Count);
		}

		cancellationToken.ThrowIfCancellationRequested();
		request.Progress?.Invoke(BuildCandidateStageId, 0, workItems.Count);
		var output = outputBuilder.Build(workItems, cancellationToken, (completed, total) =>
		{
			request.Progress?.Invoke(BuildCandidateStageId, completed, total);
		}, (unitAssetKey, elapsed) => request.Performance?.Invoke("BuildCandidate.Unit", unitAssetKey, elapsed));
		cancellationToken.ThrowIfCancellationRequested();
		if (!output.ReplacedSourceUnitAssetKeys.ToHashSet().SetEquals(sourceUnits.Keys))
		{
			throw new InvalidDataException("Reconstruction must replace every old source Unit; refusing to preserve obsolete Unit data.");
		}
		var removals = await GetAllSourceUnitAndCompositeRemovalsAsync(sourceEntries, cancellationToken).ConfigureAwait(false);
		var headerTemplate = await resolver.GetPackageTocAsync(request.Units[0].TargetArchiveId, cancellationToken).ConfigureAwait(false)
			?? throw new FileNotFoundException("The selected current target archive TOC could not be read.", request.Units[0].TargetArchiveId);
		var write = await archiveWriter.WriteAsync(
			request.SourcePatchTocPath,
			request.OutputDirectory,
			Array.Empty<PatchUnitMeshEditResult>(),
			output.AdditionalEntries,
			removals,
			preserveOriginalStream: true,
			headerTemplateTocData: headerTemplate.Data,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		request.Progress?.Invoke(WriteCandidateStageId, 1, 1);
		cancellationToken.ThrowIfCancellationRequested();
		request.Progress?.Invoke(ValidateCandidateStageId, 0, 1);
		var verificationErrors = await VerifyOutputAsync(write.TocFilePath, output, removals, request.Progress, request.Performance, cancellationToken).ConfigureAwait(false);
		if (verificationErrors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, verificationErrors));
		request.Progress?.Invoke(ValidateCandidateStageId, 1, 1);
		return new SameKeyTargetShellReconstructionResult(
			write,
			output.UnitResults.Count,
			output.UnitResults.Count(result => result.ReplacementCount > 0),
			output.UnitResults.Count(result => result.ReplacementCount == 0),
			output.UnitResults.Sum(result => result.ReplacementCount),
			output.UnitResults.Sum(result => result.MinifiedCount));
	}

	private async ValueTask<IReadOnlyList<PatchTocEntry>> GetAllSourceUnitAndCompositeRemovalsAsync(IReadOnlyList<PatchTocEntry> sourceEntries, CancellationToken cancellationToken)
	{
		var unitEntries = sourceEntries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).ToArray();
		var compositeIds = new HashSet<ulong>();
		foreach (var unit in unitEntries)
		{
			var payload = await payloadReader.ReadPayloadAsync(unit, cancellationToken).ConfigureAwait(false);
			if (payload.TocData.Length >= 24)
			{
				var compositeId = BitConverter.ToUInt64(payload.TocData, 16);
				if (compositeId != 0) compositeIds.Add(compositeId);
			}
		}
		return unitEntries.Concat(sourceEntries.Where(entry => entry.AssetKey.TypeId == CompositeUnitTypeId && compositeIds.Contains(entry.AssetKey.FileId))).ToArray();
	}

	private async ValueTask<IReadOnlyList<string>> VerifyOutputAsync(string outputTocPath, SdkStyleTargetShellPatchOutput output, IReadOnlyCollection<PatchTocEntry> removals, Action<string, long, long>? progress, Action<string, AssetKey, TimeSpan>? performance, CancellationToken cancellationToken)
	{
		var errors = new List<string>();
		var entries = await scanner.ScanEntriesAsync(outputTocPath, cancellationToken).ConfigureAwait(false);
		var unitKeys = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
		if (!unitKeys.SetEquals(output.UnitResults.Select(result => result.TargetUnitAssetKey))) errors.Add("输出 Unit 集合与批准的 current target Unit 集合不一致。");
		var outputKeys = entries.Select(entry => entry.AssetKey).ToHashSet();
		var rebuiltUnitKeys = output.UnitResults.Select(result => result.TargetUnitAssetKey).ToHashSet();
		foreach (var removed in removals)
		{
			// A same-key target Unit intentionally replaces an old source Unit in place.
			// Composite and every other removed resource must still be absent.
			if (outputKeys.Contains(removed.AssetKey) && !rebuiltUnitKeys.Contains(removed.AssetKey)) errors.Add($"输出仍包含应删除的旧资源 0x{removed.AssetKey.FileId:x16}。");
		}
		if (entries.GroupBy(entry => entry.AssetKey).Any(group => group.Count() != 1)) errors.Add("输出包含重复 AssetKey。");
		var streamLength = File.Exists(outputTocPath + ".stream") ? new FileInfo(outputTocPath + ".stream").Length : 0;
		var gpuLength = File.Exists(outputTocPath + ".gpu_resources") ? new FileInfo(outputTocPath + ".gpu_resources").Length : 0;
		foreach (var entry in entries)
		{
			if ((ulong)streamLength < entry.StreamOffset + entry.StreamSize) errors.Add($"Asset 0x{entry.AssetKey.FileId:x16} 的 stream 范围超出输出 sidecar。");
			if ((ulong)gpuLength < entry.GpuResourceOffset + entry.GpuResourceSize) errors.Add($"Asset 0x{entry.AssetKey.FileId:x16} 的 gpu_resources 范围超出输出 sidecar。");
		}
		var verifiedUnits = 0;
		foreach (var unit in output.UnitResults)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var stopwatch = Stopwatch.StartNew();
			var entry = entries.SingleOrDefault(candidate => candidate.AssetKey == unit.TargetUnitAssetKey);
			if (entry is null) { errors.Add($"输出缺少 Unit 0x{unit.TargetUnitAssetKey.FileId:x16}。"); continue; }
			var readback = await unitReader.ReadAsync(entry, entries, cancellationToken: cancellationToken).ConfigureAwait(false);
			if (readback.Model.RawMeshData.Count != unit.CoveredTargetMeshCount) errors.Add($"Unit 0x{unit.TargetUnitAssetKey.FileId:x16} readback mesh coverage differs from the rebuilt target shell.");
			foreach (var boneIndex in unit.RebuiltBoneInfoIndexes)
			{
				if (boneIndex < 0 || boneIndex >= readback.Model.BoneInfos.Count || boneIndex >= unit.BoneInfos.Count) { errors.Add($"Unit 0x{unit.TargetUnitAssetKey.FileId:x16} has an invalid rebuilt BoneInfo index."); continue; }
				var expected = unit.BoneInfos[boneIndex];
				var actual = readback.Model.BoneInfos[boneIndex];
				if (!expected.RealIndices.SequenceEqual(actual.RealIndices) || !expected.BoneMatrices.SelectMany(matrix => matrix).SequenceEqual(actual.BoneMatrices.SelectMany(matrix => matrix)) || !expected.Remaps.SelectMany(remap => remap.FakeIndices).SequenceEqual(actual.Remaps.SelectMany(remap => remap.FakeIndices))) errors.Add($"Unit 0x{unit.TargetUnitAssetKey.FileId:x16} BoneInfo {boneIndex} failed readback verification.");
			}
			progress?.Invoke(ValidateCandidateStageId, ++verifiedUnits, output.UnitResults.Count);
			performance?.Invoke("ValidateCandidate.Unit", unit.TargetUnitAssetKey, stopwatch.Elapsed);
		}
		return errors;
	}
}

public sealed record SameKeyTargetShellReconstructionRequest(
	string SourcePatchTocPath,
	string GameDataDirectory,
	string OutputDirectory,
	IReadOnlyList<SameKeyTargetShellReconstructionUnit> Units,
	IReadOnlyList<PatchTocEntry>? PreparedSourceEntries = null)
{
	public Action<string, long, long>? Progress { get; init; }
	public Action<string, AssetKey, TimeSpan>? Performance { get; init; }
	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(GameDataDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(OutputDirectory);
		ArgumentNullException.ThrowIfNull(Units);
		if (Units.Count == 0) throw new InvalidDataException("At least one approved same-key Unit is required.");
		if (Units.Select(unit => unit.UnitAssetKey).Distinct().Count() != Units.Count) throw new InvalidDataException("An approved same-key Unit appears more than once.");
		foreach (var unit in Units) unit.Validate();
	}
}

public sealed record SameKeyTargetShellReconstructionUnit(
	AssetKey UnitAssetKey,
	string TargetArchiveId,
	IReadOnlyList<TargetShellMeshMapping> MeshMappings)
{
	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(TargetArchiveId);
		ArgumentNullException.ThrowIfNull(MeshMappings);
		if (MeshMappings.Any(mapping => mapping.SourceUnitAssetKey != UnitAssetKey)) throw new InvalidDataException("Same-key mappings must source from their matching Unit AssetKey.");
		if (MeshMappings.Select(mapping => mapping.TargetMeshInfoIndex).Distinct().Count() != MeshMappings.Count) throw new InvalidDataException("A target mesh has more than one same-key mapping.");
	}
}

public sealed record SameKeyTargetShellReconstructionResult(
	PatchArchiveFileWriteResult WriteResult,
	int UnitCount,
	int UnitsWithReplacements,
	int MinifyOnlyUnitCount,
	int ReplacementMeshCount,
	int MinifiedMeshCount);
