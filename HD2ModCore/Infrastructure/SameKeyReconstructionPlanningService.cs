using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using AdaptationGameDataUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMeshReader;
using AdaptationPatchTocEntry = HD2ModAdaptation.PatchReconstruction.PatchTocEntry;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationPatchUnitMesh = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMesh;
using AdaptationPatchUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMeshReader;
using AdaptationSameKeyTargetShellMeshPlan = HD2ModAdaptation.PatchReconstruction.UnitMesh.SameKeyTargetShellMeshPlan;
using AdaptationSameKeyTargetShellPlanningOperation = HD2ModAdaptation.PatchReconstruction.UnitMesh.SameKeyTargetShellPlanningOperation;
using AdaptationSdkStyleTargetShellUnitReconstructor = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellUnitReconstructor;
using AdaptationTargetShellMeshMapping = HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellMeshMapping;

namespace HD2ModCore.Infrastructure;

// Purpose: Resolves each source Unit to a readable current game-data Unit with the exact same AssetKey and produces its reconstruction plan.
public sealed class SameKeyReconstructionPlanningService : ISameKeyReconstructionPlanningService
{
	private readonly IAssetArchiveIndexService assetIndex;
	private readonly AdaptationSameKeyTargetShellPlanningOperation planningOperation;
	private readonly AdaptationSdkStyleTargetShellUnitReconstructor reconstructor;

	public SameKeyReconstructionPlanningService(
		IAssetArchiveIndexService assetIndex,
		AdaptationSameKeyTargetShellPlanningOperation? planningOperation = null,
		AdaptationSdkStyleTargetShellUnitReconstructor? reconstructor = null)
	{
		this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex));
		this.planningOperation = planningOperation ?? new AdaptationSameKeyTargetShellPlanningOperation();
		// The writer always canonicalizes replaced target streams before encoding. The
		// dry-run must use the same route, otherwise legacy target skinning declarations
		// are rejected even though the actual current-target output is encodable.
		this.reconstructor = reconstructor ?? new AdaptationSdkStyleTargetShellUnitReconstructor(planCanonicalSkinningLayout: true);
	}

	public async ValueTask<SameKeyReconstructionPlan> CreatePlanAsync(
		SameKeyReconstructionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		request.Validate();
		var sourcePath = Path.GetFullPath(request.SourcePatchTocPath);
		var gameDataDirectory = Path.GetFullPath(request.GameDataDirectory);
		if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source patch TOC does not exist.", sourcePath);
		if (!Directory.Exists(gameDataDirectory)) throw new DirectoryNotFoundException($"Game data directory does not exist: {gameDataDirectory}");

		var sourceEntries = request.PreparedSourceEntries is { Count: > 0 }
			? request.PreparedSourceEntries.Select(ToAdaptationEntry).ToArray()
			: await new AdaptationPatchTocScanner().ScanEntriesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
		var sourceUnits = sourceEntries
			.Where(entry => entry.AssetKey.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId)
			.GroupBy(entry => entry.AssetKey)
			.Select(group => group.First())
			.OrderBy(entry => entry.AssetKey.FileId)
			.Take(request.MaxSourceUnitCount ?? int.MaxValue)
			.ToArray();
		var archiveMatches = await assetIndex.FindAssetArchivesAsync(
			sourceUnits.Select(entry => ToCoreKey(entry.AssetKey)).ToHashSet(), cancellationToken).ConfigureAwait(false);
		var archivesByKey = archiveMatches.ToDictionary(match => match.AssetKey, match => OrderArchives(match.Archives));
		var unitPlans = new List<SameKeyUnitReconstructionPlan>(sourceUnits.Length);
		var sourceReader = new AdaptationPatchUnitMeshReader();
		var targetReader = new AdaptationGameDataUnitMeshReader(new AdaptationGameDataPackageResolver(gameDataDirectory));

		foreach (var sourceEntry in sourceUnits)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var issues = new List<CoreIssue>();
			var coreSourceKey = ToCoreKey(sourceEntry.AssetKey);
			archivesByKey.TryGetValue(coreSourceKey, out var matchingArchives);
			matchingArchives ??= Array.Empty<ArchiveMetadata>();
			if (matchingArchives.Count == 0)
			{
				issues.Add(Error("CurrentTargetMissing", "Game Data index has no current archive for this same-AssetKey Unit.", sourceEntry));
				unitPlans.Add(new SameKeyUnitReconstructionPlan(coreSourceKey, ToCoreEntry(sourceEntry), null, matchingArchives, null, issues));
				continue;
			}
			AdaptationPatchUnitMesh sourceUnit;
			try
			{
				sourceUnit = await sourceReader.ReadAsync(sourceEntry, sourceEntries, cancellationToken: cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException or KeyNotFoundException)
			{
				issues.Add(Error("SourceUnitUnreadable", exception.Message, sourceEntry, exception));
				unitPlans.Add(new SameKeyUnitReconstructionPlan(coreSourceKey, ToCoreEntry(sourceEntry), null, matchingArchives, null, issues));
				continue;
			}

			ArchiveMetadata? selectedArchive = null;
			HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMesh? targetUnit = null;
			var unreadableArchives = new List<string>();
			foreach (var archive in matchingArchives)
			{
				try
				{
					targetUnit = await targetReader.ReadAsync(
						archive.ArchiveId,
						new AdaptationAssetKey(sourceEntry.AssetKey.TypeId, sourceEntry.AssetKey.FileId),
						allowGlobalDependencySearch: true,
						cancellationToken: cancellationToken).ConfigureAwait(false);
					selectedArchive = archive;
					break;
				}
				catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException or KeyNotFoundException)
				{
					unreadableArchives.Add($"{archive.ArchiveId}: {exception.Message}");
				}
			}
			if (targetUnit is null || selectedArchive is null)
			{
				var detail = unreadableArchives.Count == 0 ? string.Empty : $" {string.Join(" | ", unreadableArchives)}";
				issues.Add(Error("CurrentTargetUnreadable", $"No indexed current archive yielded a readable same-AssetKey Unit.{detail}", sourceEntry));
				unitPlans.Add(new SameKeyUnitReconstructionPlan(coreSourceKey, ToCoreEntry(sourceEntry), null, matchingArchives, null, issues));
				continue;
			}

			var targetShellPlan = planningOperation.CreatePlan(sourceUnit, targetUnit);
			var adaptation = CreateCorePlan(sourceEntry, selectedArchive, targetShellPlan, sourceUnit, targetUnit);
			var evidence = BuildMeshEvidence(sourceUnit, targetUnit, adaptation);
			var targetMeshCount = targetUnit.Model.RawMeshData.Count;
			var coveredTargetMeshCount = adaptation.Steps
				.Select(step => step.TargetMeshInfoIndex)
				.Distinct()
				.Count();
			if (adaptation.Steps.Any(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource && step.Candidate?.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback) && !request.AllowExperimentalCandidates)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ExperimentalMeshCandidate", "One or more selected mesh mappings need experimental fallback and are not eligible for automatic test-copy output.", sourceEntry.SourceFilePath));
			}
			if (!adaptation.CanWrite)
			{
				issues.Add(Error("TargetSerializationFailed", adaptation.Reason, sourceEntry));
			}
			unitPlans.Add(new SameKeyUnitReconstructionPlan(coreSourceKey, ToCoreEntry(sourceEntry), selectedArchive, matchingArchives, adaptation, issues, evidence, targetMeshCount, coveredTargetMeshCount));
		}

		var globalIssues = new List<CoreIssue>();
		if (sourceUnits.Length == 0)
		{
			globalIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "NoSourceUnits", "The source patch does not contain Unit entries.", sourcePath));
		}
		return new SameKeyReconstructionPlan(request with { SourcePatchTocPath = sourcePath, GameDataDirectory = gameDataDirectory }, unitPlans, globalIssues);
	}

	private static IReadOnlyList<ArchiveMetadata> OrderArchives(IReadOnlyList<ArchiveMetadata> archives)
		=> archives.OrderBy(archive => archive.CategoryOrder).ThenBy(archive => archive.ArchiveOrder).ThenBy(archive => archive.ArchiveId, StringComparer.OrdinalIgnoreCase).ToArray();

	private static AdaptationPatchTocEntry ToAdaptationEntry(PatchTocEntry entry)
		=> new(new AdaptationAssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), entry.SourceFilePath, entry.SourceFileName,
			entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset, entry.Unknown1, entry.Unknown2,
			entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.Unknown3, entry.Unknown4, entry.EntryIndex);

	private UnitMeshAdaptationPlan CreateCorePlan(
		AdaptationPatchTocEntry sourceEntry,
		ArchiveMetadata targetArchive,
		AdaptationSameKeyTargetShellMeshPlan targetShellPlan,
		AdaptationPatchUnitMesh sourceUnit,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMesh targetUnit)
	{
		var coreSourceEntry = ToCoreEntry(sourceEntry);
		var candidates = targetShellPlan.Candidates.Select(ToCoreCandidate).ToArray();
		var candidateByMapping = candidates.ToDictionary(candidate => (candidate.SourceMeshInfoIndex, candidate.TargetMeshInfoIndex));
		var steps = targetShellPlan.MinifiedTargetMeshInfoIndexes
			.Select(index => new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.MinifyTarget, index, null, "Target mesh is minified because it has no selected source replacement."))
			.Concat(targetShellPlan.MeshMappings.Select(mapping => new UnitMeshAdaptationStep(
				UnitMeshAdaptationStepKind.ReplaceWithSource,
				mapping.TargetMeshInfoIndex,
				mapping.SourceMeshInfoIndex,
				candidateByMapping[(mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)].Reason,
				candidateByMapping[(mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)])))
			.ToArray();
		try
		{
			reconstructor.Reconstruct(targetUnit, new[] { sourceUnit }, targetShellPlan.MeshMappings);
			return new UnitMeshAdaptationPlan(new UnitMeshAdaptationIntent(coreSourceEntry, targetArchive.ArchiveId, null), true, candidates, steps, targetShellPlan.Reason);
		}
		catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException or KeyNotFoundException)
		{
			return new UnitMeshAdaptationPlan(new UnitMeshAdaptationIntent(coreSourceEntry, targetArchive.ArchiveId, null), false, candidates, steps, $"SDK target-shell dry-run failed to serialize the planned Unit: {exception.Message}");
		}
	}

	private static IReadOnlyList<SameKeyMeshEvidence> BuildMeshEvidence(
		AdaptationPatchUnitMesh sourceUnit,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMesh targetUnit,
		UnitMeshAdaptationPlan adaptation)
	{
		var replacementTargets = adaptation.Steps
			.Where(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource)
			.Select(step => step.TargetMeshInfoIndex)
			.ToHashSet();
		var minifiedTargets = adaptation.Steps
			.Where(step => step.Kind == UnitMeshAdaptationStepKind.MinifyTarget)
			.Select(step => step.TargetMeshInfoIndex)
			.ToHashSet();
		return adaptation.Candidates.Select(candidate =>
		{
			var source = sourceUnit.Model.RawMeshData.First(mesh => mesh.MeshInfoIndex == candidate.SourceMeshInfoIndex);
			var target = targetUnit.Model.RawMeshData.First(mesh => mesh.MeshInfoIndex == candidate.TargetMeshInfoIndex);
			var sourceStream = sourceUnit.Model.Streams.First(stream => stream.Index == source.StreamIndex);
			var targetStream = targetUnit.Model.Streams.First(stream => stream.Index == target.StreamIndex);
			var sourceBones = GetRealBoneIndices(sourceUnit.Model, source);
			var targetBones = GetRealBoneIndices(targetUnit.Model, target);
			return new SameKeyMeshEvidence(
				candidate.SourceMeshInfoIndex,
				candidate.TargetMeshInfoIndex,
				candidate.Kind,
				candidate.Score,
				candidate.SourceSemanticName,
				candidate.TargetSemanticName,
				source.LodIndex,
				target.LodIndex,
				source.Vertices.Count,
				target.Vertices.Count,
				source.Triangles.Count,
				target.Triangles.Count,
				source.Sections.Count,
				target.Sections.Count,
				sourceStream.VertexStride,
				targetStream.VertexStride,
				DescribeComponents(sourceStream),
				DescribeComponents(targetStream),
				sourceBones,
				targetBones,
				sourceBones.Intersect(targetBones).Count(),
				replacementTargets.Contains(candidate.TargetMeshInfoIndex),
				minifiedTargets.Contains(candidate.TargetMeshInfoIndex),
				candidate.Reason);
		}).ToArray();
	}

	private static IReadOnlyList<string> DescribeComponents(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitStreamInfo stream)
		=> stream.Components.Select(component => $"{component.TypeName}[{component.Index}]={component.FormatName}:{component.Size}").ToArray();

	private static IReadOnlyList<uint> GetRealBoneIndices(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel model, HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitRawMeshData mesh)
	{
		if (model.BoneInfos.Count == 0)
		{
			return Array.Empty<uint>();
		}
		var boneInfoIndex = mesh.LodIndex is >= 0 and < int.MaxValue && mesh.LodIndex < model.BoneInfos.Count ? mesh.LodIndex : 0;
		var boneInfo = model.BoneInfos[boneInfoIndex];
		var fakeToReal = boneInfo.Remaps
			.SelectMany(remap => remap.FakeIndices)
			.Where(index => index < boneInfo.RealIndices.Count)
			.Select(index => boneInfo.RealIndices[(int)index])
			.ToHashSet();
		return mesh.Vertices
			.SelectMany(vertex => vertex.Components.Where(component => component.Type == 6).SelectMany(component => component.UIntValues))
			.Select(index => index < boneInfo.RealIndices.Count ? boneInfo.RealIndices[(int)index] : index)
			.Where(index => fakeToReal.Count == 0 || fakeToReal.Contains(index))
			.Distinct()
			.OrderBy(index => index)
			.ToArray();
	}

	private static UnitMeshReplacementCandidate ToCoreCandidate(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshReplacementCandidate candidate)
		=> new(candidate.TargetMeshInfoIndex, candidate.SourceMeshInfoIndex, candidate.TargetMeshId, candidate.SourceMeshId, candidate.TargetName, candidate.SourceName, candidate.LodIndex, candidate.StreamIndex, candidate.VertexStride, candidate.ComponentLayout.Select(component => new UnitMeshReplacementComponentSignature(component.Type, component.Index, component.Format, component.Size)).ToArray(), (UnitMeshReplacementCandidateKind)candidate.Kind, candidate.Score, candidate.Reason);

	private static AssetKey ToCoreKey(AdaptationAssetKey assetKey) => new(assetKey.TypeId, assetKey.FileId);

	private static PatchTocEntry ToCoreEntry(AdaptationPatchTocEntry entry)
		=> new(new AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), entry.SourceFilePath, entry.SourceFileName, entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset, entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.EntryIndex);

	private static CoreIssue Error(string code, string message, AdaptationPatchTocEntry sourceEntry, Exception? exception = null)
		=> new(CoreIssueSeverity.Error, code, message, sourceEntry.SourceFilePath, ExceptionMessage: exception?.ToString());
}